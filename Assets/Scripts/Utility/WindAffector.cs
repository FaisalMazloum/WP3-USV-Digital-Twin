using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class WindAffector : MonoBehaviour
{
    private Rigidbody rb;
    public WindController _windController;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }
    
    void FixedUpdate()
    {
        rb.AddForce(_windController.windForce, ForceMode.Force);
    }
    
}