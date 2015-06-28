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

			if (_snapshot == null)
				return;

			GUILayout.Label ("NativeTypes: " + _snapshot.nativeTypes.Length);

			if (GUILayout.Button ("Crawl")) {
				var crawler = new Crawler ();
				_packedCrawled = crawler.Crawl (_snapshot);
			}

			if (_packedCrawled == null)
				return;

			scrollPosition = GUILayout.BeginScrollView(scrollPosition);

			for(int managedIndex=0; managedIndex != _packedCrawled.managedObjects.Length; managedIndex++) {
				var mo = _packedCrawled.managedObjects [managedIndex];
				var totalIndex = managedIndex + _packedCrawled.IndexOfFirstManagedObject;
				GUILayout.Label (totalIndex + " "+_snapshot.typeDescriptions [mo.typeIndex].name + " size:" + mo.size + " address:" + mo.address);

				var connections = _packedCrawled.connections;
				for (int i=0; i!=connections.Length;i++)
				{
					if (connections[i].to == totalIndex)
						GUILayout.Label("Referenced from: "+connections[i].@from);
					if (connections[i].from == totalIndex)
						GUILayout.Label("References: "+connections[i].@to);
				}
			
			}
			GUILayout.EndScrollView ();
		}

		Vector2 scrollPosition;

		void IncomingSnapshot(UnityEditor.MemoryProfiler.PackedMemorySnapshot snapshot)
		{
			_snapshot = snapshot;
		}
	}
}

