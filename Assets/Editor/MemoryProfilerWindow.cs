using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Assets.Editor.Treemap;
using Treemap;
using UnityEditor;
using UnityEngine;
using System;
using System.Net;
using NUnit.Framework.Constraints;
using UnityEditor.MemoryProfiler;
using Object = UnityEngine.Object;

namespace MemoryProfilerWindow
{
	public class MemoryProfilerWindow : EditorWindow
	{		
		[NonSerialized]
		UnityEditor.MemoryProfiler.PackedMemorySnapshot _snapshot;

		[SerializeField]
		PackedCrawlerData _packedCrawled;

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
	    private Item _mouseDownItem;
	 	private Inspector _inspector;

	    [MenuItem("Window/MemoryProfiler")]
		static void Create()
		{
			EditorWindow.GetWindow<MemoryProfilerWindow> ();
		}

		[MenuItem("Window/MemoryProfilerInspect")]
		static void Inspect()
		{
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
			if (_unpackedCrawl == null && _packedCrawled.valid)
				Unpack ();
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
									SelectThing(firstOrDefault._thingInMemory);
                                    Event.current.Use();
                                    return;
                                }
		                        break;
                        }
		              
		               
		            }
		        }
		    }

			Rect r = new Rect(0f, 25f, position.width-_inspector.width, position.height - 25f);

            _ZoomableArea.rect = r;
			_ZoomableArea.BeginViewGUI();

			GUI.BeginGroup(r);
			Handles.matrix = _ZoomableArea.drawingToViewMatrix;
			RenderTreemap();
			GUI.EndGroup();

			_ZoomableArea.EndViewGUI();
			Repaint();

			_inspector.Draw ();
            
			//RenderDebugList();
		}

		public void SelectThing(ThingInMemory thing)
		{
			_selectedItem = _items.First (i => i._thingInMemory == thing);
			_inspector.SelectThing (thing);
			RefreshMesh ();
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

		void Unpack ()
		{
			_unpackedCrawl = CrawlDataUnpacker.Unpack (_packedCrawled);
			_inspector = new Inspector (this, _unpackedCrawl, _snapshot);
			RefreshCaches();
		}

		void IncomingSnapshot(UnityEditor.MemoryProfiler.PackedMemorySnapshot snapshot)
		{
			_snapshot = snapshot;

			var timer = new System.Diagnostics.Stopwatch ();
			timer.Start ();
			var crawler = new Crawler ();
			_packedCrawled = crawler.Crawl (_snapshot);
			Debug.Log ("Crawl: " + timer.ElapsedMilliseconds + "ms");
			Unpack ();
			timer.Reset ();
			Debug.Log ("Unpack: " + timer.ElapsedMilliseconds + "ms");

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
//			if (thing is ManagedObject)
//				return (thing as ManagedObject).typeDescription.name;
			return thing.GetType ().FullName;
		}

	}
}

