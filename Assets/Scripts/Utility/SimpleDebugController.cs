using UnityEngine;

public class SimpleDebugController : MonoBehaviour
{
    [SerializeField]
    public float moveSpeed = 0.3f;
    public float rotateSpeed = 60.0f;
    
    public Rigidbody rb;
    
    void Start()
    {
        // rb = GetComponent<Rigidbody>();
    }
    
    void FixedUpdate()
    {
        Vector3 velocity = Vector3.zero;
        
        // Forward/Backward - Up/Down arrows
        if (Input.GetKey(KeyCode.UpArrow))
        {
            velocity = rb.transform.forward * moveSpeed;
        }
        if (Input.GetKey(KeyCode.DownArrow))
        {
            velocity = -rb.transform.forward * moveSpeed;
        }
        
        rb.velocity = velocity;
        
        // Rotate - Left/Right arrows
        if (Input.GetKey(KeyCode.LeftArrow))
        {
            rb.angularVelocity = Vector3.up * -rotateSpeed * Mathf.Deg2Rad;
        }
        else if (Input.GetKey(KeyCode.RightArrow))
        {
            rb.angularVelocity = Vector3.up * rotateSpeed * Mathf.Deg2Rad;
        }
        else
        {
            rb.angularVelocity = Vector3.zero;
        }
    }
}