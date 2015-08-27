using System;
using Assets.Editor.Treemap;
using UnityEditor;
using UnityEngine;
using UnityEditor.MemoryProfiler;
using System.Linq;
using System.Collections.Generic;

namespace MemoryProfilerWindow
{
	public class Inspector
	{
		ThingInMemory _selectedThing;
		private ThingInMemory[] _shortestPath;
		private ShortestPathToRootFinder _shortestPathToRootFinder;
		private static int s_InspectorWidth = 400;
		Vector2 _scrollPosition;
		MemoryProfilerWindow _hostWindow;
		CrawledMemorySnapshot _unpackedCrawl;
		PackedMemorySnapshot _snapshot;
		PrimitiveValueReader _primitiveValueReader;

		public Inspector(MemoryProfilerWindow hostWindow, CrawledMemorySnapshot unpackedCrawl, PackedMemorySnapshot snapshot)
		{
			_unpackedCrawl = unpackedCrawl;
			_snapshot = snapshot;
			_hostWindow = hostWindow;
			_shortestPathToRootFinder = new ShortestPathToRootFinder(unpackedCrawl);
			if (snapshot == null)
				return;
			_primitiveValueReader = new PrimitiveValueReader(_snapshot.virtualMachineInformation, _snapshot.managedHeapSections);
		}

		public float width {
			get { return s_InspectorWidth; }
		}

		public void SelectThing(ThingInMemory thing)
		{
			_selectedThing = thing;
			_shortestPath = _shortestPathToRootFinder.FindFor(thing);
		}

		public void Draw()
		{
			GUILayout.BeginArea(new Rect(_hostWindow.position.width - s_InspectorWidth, 25, s_InspectorWidth, _hostWindow.position.height - 25f));
			_scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

			if (_selectedThing == null)
				GUILayout.Label("Select an object to see more info");
			else
			{
				var nativeObject = _selectedThing as NativeUnityEngineObject;
				if (nativeObject != null)
				{
					GUILayout.Label("NativeUnityEngineObject");
					GUILayout.Label("Name: "+nativeObject.name);
					GUILayout.Label("ClassName: "+nativeObject.className);
					GUILayout.Label("ClassID: "+nativeObject.classID);
					GUILayout.Label("instanceID: "+nativeObject.instanceID);
					GUILayout.Label("isDontDestroyOnLoad:"+nativeObject.isDontDestroyOnLoad);
					GUILayout.Label("isPersistent:" + nativeObject.isPersistent);
					GUILayout.Label("isManager:" + nativeObject.isManager);
					GUILayout.Label("hideFlags: "+nativeObject.hideFlags);
				}

				var managedObject = _selectedThing as ManagedObject;
				if (managedObject != null)
				{
					GUILayout.Label("ManagedObject");
					GUILayout.Label("Type: " + managedObject.typeDescription.name);
					GUILayout.Label("Address: " + managedObject.address);

					DrawFields(managedObject);

					if (managedObject.typeDescription.isArray)
					{
						DrawArray(managedObject);
					}
				}

				if (_selectedThing is GCHandle)
				{
					GUILayout.Label("GCHandle");
				}

				var staticFields = _selectedThing as StaticFields;
				if (staticFields != null)
				{
					GUILayout.Label("Static Fields");
					GUILayout.Label("Of type: "+ staticFields.typeDescription.name);

					DrawFields(staticFields.typeDescription, new BytesAndOffset() { bytes = staticFields.typeDescription.staticFieldBytes, offset = 0}, true);
				}

				if (managedObject == null)
				{
					GUILayout.Space(10);
					GUILayout.Label("References:");
					DrawLinks(_selectedThing.references);
				}

				GUILayout.Space(10);
				GUILayout.Label("Referenced by:");
				DrawLinks(_selectedThing.referencedBy);

				GUILayout.Space(10);
				if (_shortestPath != null)
				{
					if (_shortestPath.Length > 1)
					{
						GUILayout.Label("ShortestPathToRoot");
						DrawLinks(_shortestPath);
					}
					string reason;
					_shortestPathToRootFinder.IsRoot(_shortestPath.Last(), out reason);
					GUILayout.Label("This is a root because:");
					GUILayout.TextArea(reason);
				}
				else
				{
					GUILayout.TextArea("No root is keeping this object alive. It will be collected next UnloadUnusedAssets() or scene load");
				}

			}
			GUILayout.EndScrollView();
			GUILayout.EndArea();
		}

		private void DrawArray(ManagedObject managedObject)
		{
			var typeDescription = managedObject.typeDescription;
			int elementCount = _snapshot.managedHeapSections.ReadArrayLength(managedObject.address, typeDescription, _snapshot.virtualMachineInformation);
			GUILayout.Label("element count: "+elementCount);
			int rank = typeDescription.arrayRank;
			GUILayout.Label("arrayRank: "+rank);
			if (_snapshot.typeDescriptions[typeDescription.baseOrElementTypeIndex].isValueType)
			{
				GUILayout.Label("Cannot yet display elements of value type arrays");
				return;
			}
			if (rank != 1)
			{
				GUILayout.Label("Cannot display non rank=1 arrays yet.");
				return;
			}

			var pointers = new List<UInt64>();
			for (int i = 0; i != elementCount; i++)
			{
				pointers.Add(_primitiveValueReader.ReadPointer(managedObject.address + (UInt64) _snapshot.virtualMachineInformation.arrayHeaderSize + (UInt64) (i*_snapshot.virtualMachineInformation.pointerSize)));
			}
			GUILayout.Label("elements:");
			DrawLinks(pointers);
		}

		private void DrawFields(TypeDescription typeDescription, BytesAndOffset bytesAndOffset, bool useStatics = false)
		{
			foreach (var field in typeDescription.fields.Where(f => f.isStatic == useStatics))
			{
				GUILayout.Label("name: " + field.name);
				GUILayout.Label("offset: " + field.offset);
				GUILayout.Label("type: " + _snapshot.typeDescriptions[field.typeIndex].name);

				DrawValueFor(field, bytesAndOffset.Add(field.offset));

				GUILayout.Space(5);
			}
		}

		private void DrawFields(ManagedObject managedObject)
		{
			GUILayout.Space(10);
			GUILayout.Label("Fields:");
			DrawFields(managedObject.typeDescription, _snapshot.managedHeapSections.Find(managedObject.address + (UInt64)_snapshot.virtualMachineInformation.objectHeaderSize, _snapshot.virtualMachineInformation));
		}

		private void DrawValueFor(FieldDescription field, BytesAndOffset bytesAndOffset)
		{
			var typeDescription = _snapshot.typeDescriptions[field.typeIndex];

			switch (typeDescription.name)
			{
			case "System.Int32":
				GUILayout.Label(_primitiveValueReader.ReadInt32(bytesAndOffset).ToString());
				break;
			case "System.Int64":
				GUILayout.Label(_primitiveValueReader.ReadInt64(bytesAndOffset).ToString());
				break;
			case "System.UInt32":
				GUILayout.Label(_primitiveValueReader.ReadUInt32(bytesAndOffset).ToString());
				break;
			case "System.UInt64":
				GUILayout.Label(_primitiveValueReader.ReadUInt64(bytesAndOffset).ToString());
				break;
			case "System.Int16":
				GUILayout.Label(_primitiveValueReader.ReadInt16(bytesAndOffset).ToString());
				break;
			case "System.UInt16":
				GUILayout.Label(_primitiveValueReader.ReadUInt16(bytesAndOffset).ToString());
				break;
			case "System.Byte":
				GUILayout.Label(_primitiveValueReader.ReadByte(bytesAndOffset).ToString());
				break;
			case "System.SByte":
				GUILayout.Label(_primitiveValueReader.ReadSByte(bytesAndOffset).ToString());
				break;
			case "System.Char":
				GUILayout.Label(_primitiveValueReader.ReadChar(bytesAndOffset).ToString());
				break;
			case "System.Boolean":
				GUILayout.Label(_primitiveValueReader.ReadBool(bytesAndOffset).ToString());
				break;
			case "System.Single":
				GUILayout.Label(_primitiveValueReader.ReadSingle(bytesAndOffset).ToString());
				break;
			case "System.Double":
				GUILayout.Label(_primitiveValueReader.ReadDouble(bytesAndOffset).ToString());
				break;
			default:
				if (!typeDescription.isValueType)
				{
					/*
					var item = FindItemPointedToByManagedFieldAt(bytesAndOffset);
					if (item == null)
					{
						EditorGUI.BeginDisabledGroup(true);
						GUILayout.Button("Null");
						EditorGUI.EndDisabledGroup();
					}
					else
					{
						DrawLinks(new [] { item._thingInMemory });

					}*/
				}
				else
				{
					DrawFields(typeDescription, bytesAndOffset);
				}
				break;
			}
		}

		/*
		private Item FindItemPointedToByManagedFieldAt(BytesAndOffset bytesAndOffset)
		{
			var stringAddress = _primitiveValueReader.ReadPointer(bytesAndOffset);
			return 
				_items.FirstOrDefault(i =>
					{
						var m = i._thingInMemory as ManagedObject;
						if (m != null)
						{
							return m.address == stringAddress;
						}
						return false;
					});
		}*/

		private void DrawLinks(IEnumerable<UInt64> pointers)
		{
			IEnumerable<ManagedObject> things = pointers.Select (p => _unpackedCrawl.allObjects.OfType<ManagedObject> ().First (mo => mo.address == p));
			DrawLinks(things.Cast<ThingInMemory>());
		}

		private void DrawLinks(IEnumerable<ThingInMemory> thingInMemories)
		{
			var c = GUI.backgroundColor;
			GUI.skin.button.alignment = TextAnchor.UpperLeft;
			foreach (var rb in thingInMemories)
			{
				EditorGUI.BeginDisabledGroup(rb == _selectedThing);

				GUI.backgroundColor = ColorFor(rb);

				var caption = rb.caption;

				var managedObject = rb as ManagedObject;
				if (managedObject != null && managedObject.typeDescription.name == "System.String")
					caption = _primitiveValueReader.ReadString(_snapshot.managedHeapSections.Find(managedObject.address, _snapshot.virtualMachineInformation));

				if (GUILayout.Button (caption))
					_hostWindow.SelectThing (rb);
				EditorGUI.EndDisabledGroup();
			}
			GUI.backgroundColor = c;
		}

		private Color ColorFor(ThingInMemory rb)
		{
			if (rb is NativeUnityEngineObject)
				return Color.red;
			if (rb is ManagedObject)
				return Color.Lerp(Color.blue, Color.white, 0.5f);
			if (rb is GCHandle)
				return Color.magenta;
			if (rb is StaticFields)
				return Color.yellow;

			throw new ArgumentException("Unexpected type: "+rb.GetType());
		}
	}
}

