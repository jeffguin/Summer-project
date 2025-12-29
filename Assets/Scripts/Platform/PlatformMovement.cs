using UnityEngine;

public class PlatformMovement : MonoBehaviour
{
    [SerializeField] GameObject Platform;
    [SerializeField] GameObject StartPointObject;
    [SerializeField] GameObject EndPointObject;
    [SerializeField] float speed = 2f;

    Vector3 CurrentTarget;

    void Start()
    {

    }

    private void FixedUpdate()
    {
       float DistanceToStart = Vector3.Distance(Platform.transform.position, StartPointObject.transform.position);
       float DistanceToEnd = Vector3.Distance(Platform.transform.position, EndPointObject.transform.position);

        if (DistanceToStart == 0)
        {
            CurrentTarget = EndPointObject.transform.position;
            Platform.transform.Rotate(0f, 180f, 0f);
        }

        if (DistanceToEnd == 0)
        {
            CurrentTarget = StartPointObject.transform.position;
            Platform.transform.Rotate(0f, 180f, 0f);
        }

        Platform.transform.position = Vector3.MoveTowards(Platform.transform.position, CurrentTarget, speed * Time.deltaTime);
    }
}
 