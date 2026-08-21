using UnityEngine;

public class LockCanva : MonoBehaviour
{
    // Stores the world rotation you want to keep
    private Quaternion lockedRotation;

    void Start()
    {
        // Capture the rotation you set up in the editor at the start of the game
        lockedRotation = transform.rotation;
    }

    void LateUpdate()
    {
        // Force the canvas to keep its original world rotation, ignoring the parent
        transform.rotation = lockedRotation;
    }
}