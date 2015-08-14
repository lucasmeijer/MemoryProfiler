using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Assets.Editor.Treemap;
using Treemap;
using UnityEditor;
using UnityEngine;
using System;
using Object = UnityEngine.Object;

namespace MemoryProfilerWindow
{
	public class MemoryProfilerWindow : EditorWindow
	{		
		[NonSerialized]
		UnityEditor.MemoryProfiler.PackedMemorySnapshot _snapshot;
		[NonSerialized]
		PackedCrawledMemorySnapshot _packedCrawled;
		[NonSerialized]
		CrawledMemorySnapshot _unpackedCrawl;

		Vector2 _scrollPosition;
		private ZoomableArea _ZoomableArea;
		private Dictionary<string, Group> _groups;
		private List<Item> _items;
		private List<Mesh> _cachedMeshes;
		private bool _registered = false;
			
		[MenuItem("Window/MemoryProfiler")]
		static void Create()
		{
			EditorWindow.GetWindow<MemoryProfilerWindow> ();
		}

		public void OnDisable()
		{
			UnityEditor.MemoryProfiler.MemorySnapshot.OnSnapshotReceived -= IncomingSnapshot;
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
				_ZoomableArea = new ZoomableArea(true);
				_ZoomableArea.vRangeMin = -110f;
				_ZoomableArea.vRangeMax = 110f;
				_ZoomableArea.hRangeMin = -110f;
				_ZoomableArea.hRangeMax = 110f;
				_ZoomableArea.hBaseRangeMin = -110f;
				_ZoomableArea.vBaseRangeMin = -110f;
				_ZoomableArea.hBaseRangeMax = 110f;
				_ZoomableArea.vBaseRangeMax = 110f;
				_ZoomableArea.shownArea = new Rect(-110f, -110f, 220f, 220f);
			}
		}

		void OnGUI()
		{
			Initialize();

			if (GUILayout.Button ("Take Snapshot")) {
				UnityEditor.MemoryProfiler.MemorySnapshot.RequestNewSnapshot ();
			}

			Rect r = new Rect(0f, 25f, position.width, position.height - 25f);

			_ZoomableArea.rect = r;
			_ZoomableArea.BeginViewGUI();

			GUI.BeginGroup(r);
			Handles.matrix = _ZoomableArea.drawingToViewMatrix;
			RenderTreemap();
			GUI.EndGroup();

			_ZoomableArea.EndViewGUI();
			Repaint();
			//RenderDebugList();
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
			_packedCrawled = crawler.Crawl (_snapshot);

			_unpackedCrawl = CrawlDataUnpacker.Unpack (_packedCrawled);
			RefreshCaches();
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
				colors[index++] = item.color;
				colors[index++] = item.color * 0.75f;
				colors[index++] = item.color * 0.5f;
				colors[index++] = item.color * 0.75f;

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

			return "Undefined";
		}

	}
}

