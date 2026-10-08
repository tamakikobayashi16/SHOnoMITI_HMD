using UnityEngine;
using UnityEngine.SceneManagement;

// Receives the complete shodoVR example before enabling the existing writing game.
public class RemotePracticeController : MonoBehaviour
{
    public bool Ready { get; private set; }
    public int CharacterCount => characters.Length;
    private int[] characters = new int[0];
    private RemoteInkReceiver receiver;
    private Material material;
    private int activeCharacter;

    private void Awake()
    {
        receiver = gameObject.AddComponent<RemoteInkReceiver>();
        material = new Material(Shader.Find("Sprites/Default"));
        material.color = new Color(0.35f, 0.35f, 0.35f, 0.6f);
        receiver.lineMaterial = material;
        // PenPosi uses z=9.5. Preview beside the paper; all characters are retained.
        receiver.positionOffset = new Vector3(0, 0, -1.4f);
    }

    private void OnGUI()
    {
        GUI.Box(new Rect(15, 15, 430, Ready ? 110 : 155), "Remote example / UDP 5005");
        if (Ready) {
            GUI.Label(new Rect(25, 45, 400, 25), $"Trace character {activeCharacter + 1}/{characters.Length}");
            if (GUI.Button(new Rect(25, 75, 180, 35), "Back to title")) SceneManager.LoadScene("Title");
            return;
        }
        string message = receiver.ReceiveError != null ? "UDP error: " + receiver.ReceiveError : receiver.HasPacketLoss ? "Packets missing: restart sender and redraw." :
            receiver.HasExperimentEnded ? "Example finished. Ready to trace." : "Waiting for drawing. Sender: Enter to finish.";
        GUI.Label(new Rect(25, 45, 410, 25), message);
        GUI.enabled = receiver.IsListening && receiver.ReceiveError == null && receiver.HasExperimentEnded && !receiver.HasPacketLoss && receiver.Characters.Length > 0;
        if (GUI.Button(new Rect(25, 80, 180, 35), "Start tracing")) {
            characters = receiver.Characters;
            receiver.HideAll();
            Ready = true;
        }
        GUI.enabled = true;
        if (GUI.Button(new Rect(215, 80, 100, 35), "Reset")) receiver.ResetReception();
        if (GUI.Button(new Rect(325, 80, 100, 35), "Title")) SceneManager.LoadScene("Title");
    }

    public void ConfigurePreview(GameObject paper)
    {
        Vector3 center = paper != null ? paper.transform.position : new Vector3(0, 0, 8.1f);
        var renderer = paper != null ? paper.GetComponent<Renderer>() : null;
        if (renderer != null) center = renderer.bounds.center;
        receiver.positionOffset = center - new Vector3(0, 0, 9.5f) - new Vector3(0, 0, 0.02f);
    }

    public void ShowCharacter(int index, GameObject paper)
    {
        if (!Ready || index < 0 || index >= characters.Length) return;
        activeCharacter = index;
        // Preserve shodoVR's scale and aspect ratio. Translate its paper origin to ours.
        Vector3 origin = paper != null ? paper.transform.position : new Vector3(0, 0, 8.1f);
        var renderer = paper != null ? paper.GetComponent<Renderer>() : null;
        if (renderer != null) origin = renderer.bounds.center;
        Vector3 targetOffset = origin - new Vector3(0, 0, 9.5f) - new Vector3(0, 0, 0.02f);
        receiver.TranslateExample(targetOffset - receiver.positionOffset);
        receiver.positionOffset = origin - new Vector3(0, 0, 9.5f) - new Vector3(0, 0, 0.02f);
        receiver.ShowCharacter(characters[index]);
    }

    public void HideExample() { receiver.HideAll(); }
    private void OnDestroy() { if (material != null) Destroy(material); }
}
