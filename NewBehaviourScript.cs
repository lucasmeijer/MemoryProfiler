using UnityEngine;
using System.Collections;

public class NewBehaviourScript : MonoBehaviour {

	// Use this for initialization
	void Start () {
	
	}
	
	// Update is called once per frame
	void Update () {
		transform.Rotate (1, 2, 3);
	}

	object[] o;

	void OnGUI()
	{
		if (GUILayout.Button ("small")) {
			o = new object[20];
			for (int i = 0; i != o.Length; i++)
				o [i] = "lucas" + i;
			System.GC.Collect ();
		}
	}
}
