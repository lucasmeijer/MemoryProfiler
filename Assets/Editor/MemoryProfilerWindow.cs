using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Assets.Editor.Treemap;
using Treemap;
using UnityEditor;
using UnityEngine;
using System;
using System.Net;
using Assets.MemoryProfiler.Assets.Editor;
using NUnit.Framework.Constraints;
using UnityEditor.MemoryProfiler;
using Object = UnityEngine.Object;

namespace MemoryProfilerWindow
{
	public class MemoryProfilerWindow : EditorWindow
	{		
		[NonSerialized]
		UnityEditor.MemoryProfiler.PackedMemorySnapshot _snapshot;

		[NonSerialized]
		CrawledMemorySnapshot _unpackedCrawl;

		Vector2 _scrollPosition;
		private ZoomableArea _ZoomableArea;
		private Dictionary<string, Group> _groups;
		private List<Item> _items;
		private List<Mesh> _cachedMeshes;
        [NonSerialized]
		private bool _registered = false;
	    private Item _selectedItem;
	    private ThingInMemory[] _shortestPath;
	    private static int s_InspectorWidth = 400;
	    private ShortestPathToRootFinder _shortestPathToRootFinder;
	    private Item _mouseDownItem;
	    private PrimitiveValueReader _primitiveValueReader;

	    [MenuItem("Window/MemoryProfiler")]
		static void Create()
		{
			EditorWindow.GetWindow<MemoryProfilerWindow> ();
		}

		public void OnDisable()
		{
		//	UnityEditor.MemoryProfiler.MemorySnapshot.OnSnapshotReceived -= IncomingSnapshot;
		}

		public void Initialize()
		{
			if (_groups == null)
			{
				_groups = new Dictionary<string, Group>();
			}
			if (_items == null)
			{
				_items = new List<Item>();
			}
			if (!_registered)
			{
				UnityEditor.MemoryProfiler.MemorySnapshot.OnSnapshotReceived += IncomingSnapshot;
				_registered = true;
			}
			if (_ZoomableArea == null)
			{
			    _ZoomableArea = new ZoomableArea(true)
			    {
			        vRangeMin = -110f,
			        vRangeMax = 110f,
			        hRangeMin = -110f,
			        hRangeMax = 110f,
			        hBaseRangeMin = -110f,
			        vBaseRangeMin = -110f,
			        hBaseRangeMax = 110f,
			        vBaseRangeMax = 110f,
			        shownArea = new Rect(-110f, -110f, 220f, 220f)
			    };
			}
		}

		void OnGUI()
		{
			Initialize();

			if (GUILayout.Button ("Take Snapshot")) {
				UnityEditor.MemoryProfiler.MemorySnapshot.RequestNewSnapshot ();
			}

		    if (Event.current.type == EventType.MouseDrag)
		        _mouseDownItem = null;

		    if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)
		    {
                
		        if (_ZoomableArea.drawRect.Contains(Event.current.mousePosition))
		        {
		            var mousepos = Event.current.mousePosition;
		            mousepos.y -= 25;
		            var pos = _ZoomableArea.ViewToDrawingTransformPoint(mousepos);
		            var firstOrDefault = _items.FirstOrDefault(i => i._position.Contains(pos));
		            if (firstOrDefault != null)
		            {
		                switch (Event.current.type)
		                {
		                    case EventType.MouseDown:
		                        _mouseDownItem = firstOrDefault;
		                        break;

                            case EventType.MouseUp:
		                        if (_mouseDownItem == firstOrDefault)
		                        {
		                            Select(firstOrDefault);
                                    Event.current.Use();
                                    return;
                                }
		                        break;
                        }
		              
		               
		            }
		        }
		    }

		    Rect r = new Rect(0f, 25f, position.width-s_InspectorWidth, position.height - 25f);

            _ZoomableArea.rect = r;
			_ZoomableArea.BeginViewGUI();

			GUI.BeginGroup(r);
			Handles.matrix = _ZoomableArea.drawingToViewMatrix;
			RenderTreemap();
			GUI.EndGroup();

			_ZoomableArea.EndViewGUI();
			Repaint();

		    DrawInspector();
            
			//RenderDebugList();
		}

	    private void Select(Item item)
	    {
	        _selectedItem = item;
	        RefreshMesh();
            
	        _shortestPath = _shortestPathToRootFinder.FindFor(item._thingInMemory);
	    }

	    private void DrawInspector()
	    {
	        GUILayout.BeginArea(new Rect(position.width - s_InspectorWidth, 25, s_InspectorWidth, position.height - 25f));
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
	        if (_selectedItem == null)
	            GUILayout.Label("Select an object to see more info");
	        else
	        {

	            var thing = _selectedItem._thingInMemory;
	            var nativeObject = thing as NativeUnityEngineObject;
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

                var managedObject = thing as ManagedObject;
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

	            if (thing is GCHandle)
	            {
	                GUILayout.Label("GCHandle");
	            }

	            var staticFields = thing as StaticFields;
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
                    DrawLinks(thing.references);
                }

                GUILayout.Space(10);
                GUILayout.Label("Referenced by:");
   	            DrawLinks(thing.referencedBy);

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
	                     
	                    }
	                }
	                else
	                {
	                    DrawFields(typeDescription, bytesAndOffset);
	                }
	                break;
	        }
	    }

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
	    }

	    private void DrawLinks(IEnumerable<UInt64> pointers)
	    {
	        var thingInMemories = pointers.Select(p => _items.FirstOrDefault(i =>
	        {
	            var m = i._thingInMemory as ManagedObject;
	            if (m != null)
	            {
	                return m.address == p;
	            }
	            return false;
	        })._thingInMemory);

	        DrawLinks(thingInMemories);
	    }

	    private void DrawLinks(IEnumerable<ThingInMemory> thingInMemories)
	    {
	        var c = GUI.backgroundColor;
            GUI.skin.button.alignment = TextAnchor.UpperLeft;
            foreach (var rb in thingInMemories)
	        {
               EditorGUI.BeginDisabledGroup(rb == _selectedItem._thingInMemory);

	            GUI.backgroundColor = ColorFor(rb);

	            var caption = rb.caption;

	            var managedObject = rb as ManagedObject;
	            if (managedObject != null && managedObject.typeDescription.name == "System.String")
                    caption = _primitiveValueReader.ReadString(_snapshot.managedHeapSections.Find(managedObject.address, _snapshot.virtualMachineInformation));

	            if (GUILayout.Button(caption))
	                Select(_items.First(i => i._thingInMemory == rb));
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

	    private void RenderDebugList()
		{
			_scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

			foreach (var thing in _unpackedCrawl.allObjects)
			{
				var mo = thing as ManagedObject;
				if (mo != null)
					GUILayout.Label("MO: " + mo.typeDescription.name);

				var gch = thing as GCHandle;
				if (gch != null)
					GUILayout.Label("GCH: " + gch.caption);

				var sf = thing as StaticFields;
				if (sf != null)
					GUILayout.Label("SF: " + sf.typeDescription.name);
			}

			GUILayout.EndScrollView();
		}

		void IncomingSnapshot(UnityEditor.MemoryProfiler.PackedMemorySnapshot snapshot)
		{
			_snapshot = snapshot;

			var crawler = new Crawler ();
			PackedCrawlerData packedCrawled = crawler.Crawl (_snapshot);

			_unpackedCrawl = CrawlDataUnpacker.Unpack (packedCrawled);
			RefreshCaches();
            _shortestPathToRootFinder = new ShortestPathToRootFinder(_unpackedCrawl);
            _primitiveValueReader = new PrimitiveValueReader(_snapshot.virtualMachineInformation, _snapshot.managedHeapSections);
        }

		void RefreshCaches()
		{
			_items.Clear();
			_groups.Clear();

			foreach (ThingInMemory thingInMemory in _unpackedCrawl.allObjects)
			{
				string groupName = GetGroupName(thingInMemory);
				if (groupName.Length == 0)
					continue;

				if (!_groups.ContainsKey(groupName))
				{
					Group newGroup = new Group();
					newGroup._name = groupName;
					newGroup._items = new List<Item>();
					_groups.Add(groupName, newGroup);
				}

				Item item = new Item(thingInMemory, _groups[groupName]);
				_items.Add(item);
				_groups[groupName]._items.Add(item);
			}

			foreach (Group group in _groups.Values)
			{
				group._items.Sort();
			}

			_items.Sort();
			RefreshCachedRects();
		}

		private void RefreshCachedRects()
		{
			Rect space = new Rect(-100f, -100f, 200f, 200f);

			List<Group> groups = _groups.Values.ToList();
			groups.Sort();
			float[] groupTotalValues = new float[groups.Count];
			for (int i = 0; i < groups.Count; i++)
			{
				groupTotalValues[i] = groups.ElementAt(i).totalMemorySize;
			}

			Rect[] groupRects = Utility.GetTreemapRects(groupTotalValues, space);
			for (int groupIndex = 0; groupIndex < groupRects.Length; groupIndex++)
			{
				Group group = groups[groupIndex];
				group._position = groupRects[groupIndex];
				Rect[] rects = Utility.GetTreemapRects(group.memorySizes, groupRects[groupIndex]);
				for (int i = 0; i < rects.Length; i++)
				{
					group._items[i]._position = rects[i];
				}
			}

			RefreshMesh();
		}

		private void RefreshMesh()
		{
			if (_cachedMeshes == null)
			{
				_cachedMeshes = new List<Mesh>();
			}
			else
			{
				for (int i = 0; i < _cachedMeshes.Count; i++)
				{
					Object.DestroyImmediate(_cachedMeshes[i]);
				}
				_cachedMeshes.Clear();
			}

			const int maxVerts = 32000;
			Vector3[] vertices = new Vector3[maxVerts];
			Color[] colors = new Color[maxVerts];
			int[] triangles = new int[maxVerts * 6 / 4];

			int itemIndex = 0;
			foreach (Item item in _items)
			{
				int index = itemIndex * 4;
				vertices[index++] = new Vector3(item._position.xMin, item._position.yMin, 0f);
				vertices[index++] = new Vector3(item._position.xMax, item._position.yMin, 0f);
				vertices[index++] = new Vector3(item._position.xMax, item._position.yMax, 0f);
				vertices[index++] = new Vector3(item._position.xMin, item._position.yMax, 0f);

				index = itemIndex * 4;
			    var color = item.color;
			    if (item == _selectedItem)
			        color *= 1.5f;

                colors[index++] = color;
				colors[index++] = color * 0.75f;
				colors[index++] = color * 0.5f;
				colors[index++] = color * 0.75f;

				index = itemIndex * 6;
				triangles[index++] = itemIndex * 4 + 0;
				triangles[index++] = itemIndex * 4 + 1;
				triangles[index++] = itemIndex * 4 + 3;
				triangles[index++] = itemIndex * 4 + 1;
				triangles[index++] = itemIndex * 4 + 2;
				triangles[index++] = itemIndex * 4 + 3;

				itemIndex++;

				if (itemIndex >= maxVerts / 4 || itemIndex == _items.Count)
				{
					Mesh mesh = new Mesh();
					mesh.hideFlags = HideFlags.HideAndDontSave;
					mesh.vertices = vertices;
					mesh.triangles = triangles;
					mesh.colors = colors;
					_cachedMeshes.Add(mesh);

					vertices = new Vector3[maxVerts];
					colors = new Color[maxVerts];
					triangles = new int[maxVerts * 6 / 4];

					itemIndex = 0;
				}
			}
		}

		public void RenderTreemap()
		{
			if (_cachedMeshes == null)
				return;

			Material mat = (Material)EditorGUIUtility.LoadRequired("SceneView/2DHandleLines.mat");
			mat.SetPass(0);

			for (int i = 0; i < _cachedMeshes.Count; i++)
			{
				Graphics.DrawMeshNow(_cachedMeshes[i], Handles.matrix);
			}
			RenderLabels();
		}

		private void RenderLabels()
		{
			if (_groups == null)
				return;

			GUI.color = Color.black;
			Matrix4x4 mat = _ZoomableArea.drawingToViewMatrix;

			foreach (Group group in _groups.Values)
			{
				if (Utility.IsInside(group._position, _ZoomableArea.shownArea))
				{
					foreach (Item item in group._items)
					{
						Vector3 p1 = mat.MultiplyPoint(new Vector3(item._position.xMin, item._position.yMin));
						Vector3 p2 = mat.MultiplyPoint(new Vector3(item._position.xMax, item._position.yMax));

						if (p2.x - p1.x > 30f)
						{
							Rect rect = new Rect(p1.x, p2.y, p2.x - p1.x, p1.y - p2.y);
							string row1 = item._group._name;
							string row2 = EditorUtility.FormatBytes(item.memorySize);
							GUI.Label(rect, row1 + "\n" + row2);
						}
					}
				}
			}

			GUI.color = Color.white;
		}

		public string GetGroupName(ThingInMemory thing)
		{
			if (thing is NativeUnityEngineObject)
				return (thing as NativeUnityEngineObject).className;
			if (thing is ManagedObject)
				return (thing as ManagedObject).typeDescription.name;
			if (thing is GCHandle)
				return "GCHandle";
			if (thing is StaticFields)
				return "static fields";
            throw new ArgumentException("Unknown ThingInMemory: "+thing.GetType());
		}

	}
}

