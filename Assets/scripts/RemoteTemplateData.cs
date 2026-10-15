using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class RemoteTemplateData
{
    [Serializable]
    public class Stroke
    {
        public int character, stroke;
        public List<Vector3> points = new List<Vector3>();
    }
    public string promptWord;
    public string trialId;
    public bool isLocalTest;
    public Vector2 sourceHalfSize;
    public float lineWidth;
    public List<Stroke> strokes = new List<Stroke>();
}
