using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeEditorFramework
{
	/// <summary>
	/// Base class for all canvas types
	/// </summary>
	public abstract class NodeCanvas : ScriptableObject
	{
		public virtual string canvasName { get { return "DEFAULT"; } }

		public virtual bool allowSceneSaveOnly { get { return false; } }

		public virtual bool allowRecursion { get { return false; } }

		public NodeCanvasTraversal Traversal;

		public NodeEditorState[] editorStates = new NodeEditorState[0];

		public string saveName;
		public string savePath;

		public bool livesInScene = false;

		public List<Node> nodes = [];
		public List<NodeGroup> groups = [];

		[NonSerialized]
		public List<ScriptableObject> SOMemoryDump = [];

		#region Constructors

		/// <summary>
		/// Creates a canvas of the specified generic type
		/// </summary>
		public static T CreateCanvas<T> () where T : NodeCanvas
		{
			if (typeof(T) == typeof(NodeCanvas))
				throw new Exception ("Cannot create canvas of type 'NodeCanvas' as that is only the base class. Please specify a valid subclass!");
			T canvas = ScriptableObject.CreateInstance<T>();
			canvas.name = canvas.saveName = "New " + canvas.canvasName;

			NodeEditor.BeginEditingCanvas (canvas);
			canvas.OnCreate ();
			NodeEditor.EndEditingCanvas ();
			return canvas;
		}

		/// <summary>
		/// Creates a canvas of the specified canvasType as long as it is a subclass of NodeCanvas
		/// </summary>
		public static NodeCanvas CreateCanvas (Type canvasType)
		{
			NodeCanvas canvas;
			if (canvasType != null && canvasType.IsSubclassOf (typeof(NodeCanvas)))
				canvas = ScriptableObject.CreateInstance (canvasType) as NodeCanvas;
			else
				return null;
			canvas.name = canvas.saveName = "New " + canvas.canvasName;

			NodeEditor.BeginEditingCanvas (canvas);
			canvas.OnCreate ();
			NodeEditor.EndEditingCanvas ();
			return canvas;
		}

		#endregion

		#region Extension Methods

		// GENERAL

		protected virtual void OnCreate () {}

		protected virtual void ValidateSelf () { }

		public virtual void OnBeforeSavingCanvas () { }

		public virtual bool CanAddNode (string nodeID, bool isEditorAction) { return true; }

		public virtual bool CanDeleteNode (Node node) { return true; }

		public virtual bool CanOpenContextMenu(ContextType type) { return true; }

		// GUI

		public virtual void DrawCanvasPropertyEditor () { }

		// ADDITIONAL SERIALIZATION

		/// <summary>
		/// Should return all additional ScriptableObjects this Node references
		/// </summary>
		public virtual ScriptableObject[] GetScriptableObjects () { return new ScriptableObject[0]; }

		/// <summary>
		/// Replaces all references to any ScriptableObjects this Node holds with the cloned versions in the serialization process.
		/// </summary>
		protected internal virtual void CopyScriptableObjects (System.Func<ScriptableObject, ScriptableObject> replaceSO) {}


		#endregion

		#region Methods

		/// <summary>
		/// Trigger traversal of the whole canvas
		/// </summary>
		public void TraverseAll ()
		{
			Traversal?.TraverseAll ();
		}

		/// <summary>
		/// Fully clear calculation data of the whole canvas
		/// </summary>
		public void ClearAll ()
		{
			Traversal?.ClearAll ();
		}

		/// <summary>
		/// Specifies a node change, usually triggering traversal from that node
		/// </summary>
		public virtual void OnNodeChange (Node node)
		{
			if (Traversal != null && node != null)
				Traversal.OnChange (node);
		}

		/// <summary>
		/// Validates this canvas, checking for any broken nodes or references and cleans them.
		/// </summary>
		public bool Validate (bool repair = true)
		{
			NodeEditor.checkInit(false);

			// Check Groups
			if (!CheckNodeCanvasList(ref groups, "groups", repair) && !repair) return false;

			// Check Nodes
			if (!CheckNodeCanvasList(ref nodes, "nodes", repair) && !repair) return false;

			// Check Connection ports
			foreach (Node node in nodes)
			{
				ConnectionPortManager.UpdateConnectionPorts(node);
				if (node.canvas != this && !repair) return false;
				node.canvas = this;
				foreach (ConnectionPort port in node.connectionPorts)
					if (!port.Validate(node, repair) && !repair) return false;
			}

			// Check EditorStates
			if (editorStates == null)
				editorStates = new NodeEditorState[0];
			editorStates = editorStates.Where ((NodeEditorState state) => state != null).ToArray ();
			foreach (NodeEditorState state in editorStates)
			{
				if (!nodes.Contains (state.selectedNode))
					state.selectedNode = null;
			}

			// Validate CanvasType-specific stuff
			ValidateSelf ();

			return true;
		}

		/// <summary>
		/// Checks the specified list and assures it is initialized, contains no null nodes and it it does, removes them and outputs an error.
		/// </summary>
		private bool CheckNodeCanvasList<T> (ref List<T> list, string listName, bool repair)
		{
			if (list == null)
			{
				Debug.LogWarning("NodeCanvas '" + name + "' " + listName + " were erased and set to null! Automatically fixed!");
				list = [];
			}
			int originalCount = list.Count;
			for (int i = 0; i < list.Count; i++) {
				if (list[i] == null) {
					if (!repair) return false;
					list.RemoveAt(i);
					i--;
				}
			}
			if (originalCount != list.Count)
				Debug.LogWarning("NodeCanvas '" + name + "' contained " + (originalCount - list.Count) + " broken (null) " + listName + "! Automatically fixed!");
			return originalCount == list.Count;
		}

		/// <summary>
		/// Updates the source of this canvas to the specified path, updating saveName and savePath aswell as livesInScene when prefixed with "SCENE/"
		/// </summary>
		public void UpdateSource (string path)
		{
			if (path == savePath)
				return;
			string newName;
			if (path.StartsWith ("SCENE/"))
			{
				newName = path.Substring (6);
			}
			else
			{
				int nameStart = Mathf.Max(path.LastIndexOf ('/'), path.LastIndexOf ('\\'))+1;
				newName = path.Substring (nameStart, path.Length-nameStart-6);
			}
			if (!newName.ToLower ().Contains ("lastsession") && !newName.ToLower ().Contains ("cursession"))
			{
				savePath = path;
				saveName = newName;
				livesInScene = path.StartsWith ("SCENE/");
			}
		}

		#endregion
	}
}
