using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NetworkPlayer : MonoBehaviour
{
    [SerializeField]
    Rigidbody rigidbody3D;

    [SerializeField]
    ConfigurableJoint mainJoint;

    [SerializeField]
    Animator animator;

    //Input
    Vector2 moveInputVector = Vector2.zero;
    bool isJumpButtonPressed = false;

    //Controller settings
    float maxSpeed = 3;

    //States
    bool isGrounded = false;

    //Raycasts
    RaycastHit[] raycastHits = new RaycastHit[10];

    //Syncing of physics objects
    SyncPhysicsObject[] syncPhysicsObjects;

    void Awake()
    {
        syncPhysicsObjects = GetComponentsInChildren<SyncPhysicsObject>();
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        //Move input
        moveInputVector.x = Input.GetAxis("Horizontal");
        moveInputVector.y = Input.GetAxis("Vertical");


        if (Input.GetKeyDown(KeyCode.Space))
            isJumpButtonPressed = true;
    }

    void FixedUpdate()
    {
        //Assume that we are not grounded. 
        isGrounded = false;

        //Check if we are grounded
        int numberOfHits = Physics.SphereCastNonAlloc(rigidbody3D.position, 0.1f, transform.up * -1, raycastHits, 0.5f);

        //Check for valid results
        for (int i = 0; i < numberOfHits; i++)
        {
            //Ignore self hits
            if (raycastHits[i].transform.root == transform)
                continue;

            isGrounded = true;

            break;
        }

        //Apply extra gravity to charcter to make it less floaty
        if (!isGrounded)
            rigidbody3D.AddForce(Vector3.down * 10);

        float inputMagnitued = moveInputVector.magnitude;

        Vector3 localVelocifyVsForward = transform.forward * Vector3.Dot(transform.forward, rigidbody3D.velocity);

        float localForwardVelocity = localVelocifyVsForward.magnitude;

        if (inputMagnitued != 0)
        {
            Quaternion desiredDirection = Quaternion.LookRotation(new Vector3(moveInputVector.x, 0, moveInputVector.y * -1), transform.up);

            //Rotate target towards direction
            mainJoint.targetRotation = Quaternion.RotateTowards(mainJoint.targetRotation, desiredDirection, Time.fixedDeltaTime * 300);

            if(localForwardVelocity < maxSpeed)
            {
                //Move the character in the direction it is facing
                rigidbody3D.AddForce(transform.forward * inputMagnitued * 30);
            }
        }

        if(isGrounded && isJumpButtonPressed)
        {
            rigidbody3D.AddForce(Vector3.up * 20, ForceMode.Impulse);

            isJumpButtonPressed = false;
        }

        animator.SetFloat("movementSpeed", localForwardVelocity * 0.4f);


        //Update the joints rotation based on the animations
        for (int i = 0; i < syncPhysicsObjects.Length; i++)
        {
            syncPhysicsObjects[i].UpdateJointFromAnimation();
        }

    }
}
