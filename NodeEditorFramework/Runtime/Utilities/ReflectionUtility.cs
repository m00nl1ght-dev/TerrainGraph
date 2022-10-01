using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace NodeEditorFramework.Utilities 
{
	public static class ReflectionUtility
	{
		private static readonly HashSet<Assembly> SearchableAssemblies = new();
		private static readonly Dictionary<string, string> IdentifierReplacements = new();

		static ReflectionUtility()
		{
			SearchableAssemblies.Add(typeof(ReflectionUtility).Assembly);
		}

		public static void AddSearchableAssembly(Assembly assembly)
		{
			SearchableAssemblies.Add(assembly);
		}
		
		public static void AddIdentifierReplacement(string oldName, string newName)
		{
			IdentifierReplacements[oldName] = newName;
		}

		public static string ApplyIdentifierReplacements(string input)
		{
			return IdentifierReplacements.Aggregate(input, (current, replacement) => current.Replace(replacement.Key, replacement.Value));
		}
		
		public class ReflectionSearchIgnoreAttribute : Attribute
		{
			public ReflectionSearchIgnoreAttribute () { }
		}

		/// <summary>
		/// Gets all script assemblies (non-unity, non-system, etc), can contain few false positives
		/// </summary>
		public static Assembly[] getScriptAssemblies () 
		{
			return SearchableAssemblies
				.Where ((Assembly assembly) => !assembly.FullName.StartsWith ("Unity") && assembly.FullName.EndsWith ("null"))
				//.Where ((Assembly assembly) => assembly.FullName.Contains ("Assembly"))
				.ToArray();
		}

		/// <summary>
		/// Gets all non-abstract types extending the given base type
		/// </summary>
		public static Type[] getSubTypes (Type baseType) 
		{
			return getScriptAssemblies()
				.SelectMany ((Assembly assembly) => assembly.GetTypes ()
					.Where ((Type T) => 
						(T.IsClass && !T.IsAbstract) 
						&& T.IsSubclassOf (baseType)
						&& !T.GetCustomAttributes (typeof(ReflectionSearchIgnoreAttribute), false).Any ())
				).ToArray ();
		}

		/// <summary>
		/// Gets all non-abstract types extending the given base type and with the given attribute
		/// </summary>
		public static Type[] getSubTypes (Type baseType, Type hasAttribute) 
		{
			return getScriptAssemblies()
				.Where ((Assembly assembly) => !assembly.FullName.StartsWith ("Unity") && assembly.FullName.EndsWith ("null"))
				//.Where ((Assembly assembly) => assembly.FullName.Contains ("Assembly"))
				.SelectMany ((Assembly assembly) => assembly.GetTypes ()
					.Where ((Type T) => 
						(T.IsClass && !T.IsAbstract) 
						&& T.IsSubclassOf (baseType)
						&& T.GetCustomAttributes (hasAttribute, false).Any ()
						&& !T.GetCustomAttributes (typeof(ReflectionSearchIgnoreAttribute), false).Any ())
				).ToArray ();
		}

		/// <summary>
		/// Returns all fields that should be serialized in the given type
		/// </summary>
		public static FieldInfo[] getSerializedFields (Type type) 
		{
			return type.GetFields (BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				.Where ((FieldInfo field) => 
					(field.IsPublic && !field.GetCustomAttributes (typeof(NonSerializedAttribute), true).Any ())
					|| field.GetCustomAttributes (typeof(SerializeField), true).Any ()
					&& !field.GetCustomAttributes (typeof(ReflectionSearchIgnoreAttribute), false).Any ())
				.ToArray ();
		}

		/// <summary>
		/// Returns all fields that should be serialized in the given type, minus the fields declared in or above the given base type
		/// </summary>
		public static FieldInfo[] getSerializedFields (Type type, Type hiddenBaseType) 
		{
			return type.GetFields (BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				.Where ((FieldInfo field) => 
					(hiddenBaseType == null || !field.DeclaringType.IsAssignableFrom (hiddenBaseType))
					&& ((field.IsPublic && !field.GetCustomAttributes (typeof(NonSerializedAttribute), true).Any ()) 
						|| field.GetCustomAttributes (typeof(SerializeField), true).Any ()
						&& !field.GetCustomAttributes (typeof(ReflectionSearchIgnoreAttribute), false).Any ()))
				.ToArray ();
		}

		/// <summary>
		/// Gets all fields in the classType of the specified fieldType
		/// </summary>
		public static FieldInfo[] getFieldsOfType (Type classType, Type fieldType) 
		{
			return classType.GetFields (BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				.Where ((FieldInfo field) => 
					field.FieldType == fieldType || field.FieldType.IsSubclassOf (fieldType)
					&& !field.GetCustomAttributes (typeof(ReflectionSearchIgnoreAttribute), false).Any ())
				.ToArray ();
		}
	}
}