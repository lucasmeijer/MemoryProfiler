using UnityEditor;
using UnityEngine;
using System;

namespace MemoryProfilerWindow
{
	public class MemoryProfilerWindow : EditorWindow
	{
		static bool _registered = false;
		[NonSerialized]
		UnityEditor.MemoryProfiler.PackedMemorySnapshot _snapshot;
		[NonSerialized]
		PackedCrawledMemorySnapshot _packedCrawled;
		[NonSerialized]
		CrawledMemorySnapshot _unpackedCrawl;

		[MenuItem("Window/MemoryProfiler")]
		static void Create()
		{
			EditorWindow.GetWindow<MemoryProfilerWindow> ();
		}


		void OnGUI()
		{
			if (!_registered) {
				UnityEditor.MemoryProfiler.MemorySnapshot.OnSnapshotReceived += IncomingSnapshot;
				_registered = true;
			}
			if (GUILayout.Button ("Take Snapshot")) {
				UnityEditor.MemoryProfiler.MemorySnapshot.RequestNewSnapshot ();
			}
				
			if (_unpackedCrawl == null)
				return;

			//here we have all the information we could ever want, we only need
			//to display it :)

			scrollPosition = GUILayout.BeginScrollView(scrollPosition);

			foreach (var thing in _unpackedCrawl.allObjects) {
				var mo = thing as ManagedObject;
				if (mo != null)
					GUILayout.Label ("MO: " + mo.typeDescription.name);

				var gch = thing as GCHandle;
				if (gch != null)
					GUILayout.Label ("GCH: " + gch.caption);

				var sf = thing as StaticFields;
				if (sf != null)
					GUILayout.Label ("SF: " + sf.typeDescription.name);
				
			}

			GUILayout.EndScrollView ();
		}

		Vector2 scrollPosition;

		void IncomingSnapshot(UnityEditor.MemoryProfiler.PackedMemorySnapshot snapshot)
		{
			_snapshot = snapshot;

			var crawler = new Crawler ();
			_packedCrawled = crawler.Crawl (_snapshot);

			_unpackedCrawl = CrawlDataUnpacker.Unpack (_packedCrawled);
		}
	}
}

