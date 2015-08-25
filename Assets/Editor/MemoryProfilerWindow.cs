using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Assets.Editor.Treemap;
using Treemap;
using UnityEditor;
using UnityEngine;
using System;
using Assets.MemoryProfiler.Assets.Editor;
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



		    if (Event.current.type == EventType.MouseUp)
		    {
		        var mousepos = Event.current.mousePosition;
		        mousepos.y -= 25;
		        var pos = _ZoomableArea.ViewToDrawingTransformPoint(mousepos);
		        //pos.y = 25;
		        var firstOrDefault = _items.FirstOrDefault(i => i._position.Contains(pos));
		        if (firstOrDefault != null)
		        {
		            Select(firstOrDefault);
		            Event.current.Use();
		            return;
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
	            }

                GUILayout.Space(10);

                GUILayout.Label("Referenced by:");
   	            DrawLinks(thing.referencedBy);

	            GUILayout.Space(10);
                GUILayout.Label("References:");
	            DrawLinks(thing.references);

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
            GUILayout.EndArea();
	    }

	    private void DrawLinks(ThingInMemory[] thingInMemories)
	    {
	        var c = GUI.backgroundColor;
	        foreach (var rb in thingInMemories)
	        {
               EditorGUI.BeginDisabledGroup(rb == _selectedItem._thingInMemory);

	            GUI.backgroundColor = ColorFor(rb);

                if (GUILayout.Button(rb.caption))
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

