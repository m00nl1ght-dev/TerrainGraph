using System;

namespace NodeEditorFramework
{
	[Serializable]
	public abstract class NodeCanvasTraversal
	{
		public NodeCanvas nodeCanvas;

		protected NodeCanvasTraversal (NodeCanvas canvas)
		{
			nodeCanvas = canvas;
		}

		public virtual void OnLoadCanvas () { }
		public virtual void OnSaveCanvas () { }

		public abstract void TraverseAll ();
		public abstract void ClearAll();
		public virtual void OnChange (Node node) {} }
}

