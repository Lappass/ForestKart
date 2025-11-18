using UnityEngine;
using System;
using System.Collections;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
public class KartController : MonoBehaviour
{
    public float gas;
    public float brake;
    public Vector2 steer;
    private Rigidbody rb;
    public WheelCollider[] driveWheels;
    public GameObject[] driveWheelMeshes;
    public float DriveTorque = 100;
    public float BrakeTorque = 500;
    private float forwardTorque;
    public float Downforce = 100f; 
    public float SteerAngle = 30f;
    public bool BrakeAssist = false;
    public bool grounded = false;
    public bool reverse = false;
    public GameObject taillight;
    public Vector2 drift;
    private WheelFrictionCurve curve;
    private bool curveChange = false;
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        taillight.GetComponent<Renderer>().material.DisableKeyword("_EMISSION");
    }
    private void Update()
    {
        Drive(gas, brake,steer,drift);
        AddDownForce(); 
    }
    public void OnAccelerate(InputValue value)
    {
        if (value.isPressed)
        {
            gas = 1;
        }
        if(!value.isPressed)
        {
            gas = 0;
        }
    }
    public void OnBrake(InputValue value)
    {
        if (value.isPressed)
        {
            brake = 1;
            taillight.GetComponent<Renderer>().material.EnableKeyword("_EMISSION");
            if (BrakeAssist)
            {
                rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            }
        }
        if (!value.isPressed)
        {
            brake = 0;
            taillight.GetComponent<Renderer>().material.DisableKeyword("_EMISSION");
            if (BrakeAssist)
            {
                rb.constraints = RigidbodyConstraints.None;
            }
        }
    }
    public void OnSteering(InputValue value)
    {
        steer = value.Get<Vector2>();
    }
    public void OnReverse(InputValue value)
    {
        if (reverse)
        {
            reverse = false;
        }
        else
        {
            reverse = true; 
        }
    }
    public void OnReset()
    {
        transform.rotation = new Quaternion(0, 0, 0, 0);
    }
    public void OnDrift(InputValue value)
    {
        drift = value.Get<Vector2>().normalized;
    }
    private void Drive(float acceleration, float brake,Vector2 steer, Vector2 drift)
    {
        if (!reverse)
        {
            forwardTorque = acceleration * DriveTorque;
        }
        else if (reverse)
        {
            forwardTorque = -acceleration * DriveTorque;
        }
            brake *= BrakeTorque;
        steer.x = steer.x * SteerAngle;
        for (int i = 0;i< driveWheels.Length; i++)
        {
            Vector3 wheelposition;
            Quaternion wheelrotation;
            driveWheels[i].GetWorldPose(out wheelposition, out wheelrotation);
            driveWheelMeshes[i].transform.position = wheelposition;
            driveWheelMeshes[i].transform.rotation = wheelrotation;
        }
        for(int i = 0; i < driveWheels.Length; i++)
        {
            driveWheels[i].motorTorque = forwardTorque;
            driveWheels[i].brakeTorque = brake;
            if(i < 2)
            {
                driveWheels[i].steerAngle = steer.x;
            }
        }
        if (drift.x > 0)
        {
             rb.angularVelocity = new Vector3(0,0.5f,0);
             if(curveChange == false)
             {
                curveChange = true;
                SteerAngle = 40f;
                for(int i = 2; i < driveWheels.Length; i++)
                {
                    curve = driveWheels[i].sidewaysFriction;
                    curve.stiffness = 0.5f;
                    curve.extremumSlip = 8.0f;
                    driveWheels[i].sidewaysFriction = curve;
                }
             }
        }
        if (drift.x < 0)
        {
            rb.angularVelocity = new Vector3(0, -0.5f, 0);
            if(curveChange == false)
            {
                curveChange = true;
                SteerAngle = 40f;
                for(int i = 2; i < driveWheels.Length; i++)
                {
                    curve = driveWheels[i].sidewaysFriction;
                    curve.stiffness = 0.5f;
                    curve.extremumSlip = 8.0f;
                    driveWheels[i].sidewaysFriction = curve;
                }
            }
        }
        if(drift.x == 0)
        {
            if(curveChange == true)
            {
                curveChange = false;
                SteerAngle = 30f;
                for(int i = 2; i < driveWheels.Length; i++)
                {
                    curve = driveWheels[i].sidewaysFriction;
                    curve.stiffness = 1.0f;
                    curve.extremumSlip = 0.2f;
                    driveWheels[i].sidewaysFriction = curve;
                }
            }
        }
    }
    private void AddDownForce()
    {
        if (grounded)
        {
            rb.AddForce(-transform.up * Downforce * rb.linearVelocity.magnitude);
        }
        for (int i = 2; i < driveWheels.Length; i++)
        {
            WheelHit wheelHit;
            driveWheels[i].GetGroundHit(out wheelHit);
            if (wheelHit.normal == Vector3.zero)
            {
                grounded = false;
                StartCoroutine(SetConstraints());
            }
            else
            {
                grounded = true;
            }
        }
    }
    IEnumerator SetConstraints()
    {
        yield return new WaitForSeconds(0.1f);
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        yield return new WaitForSeconds(0.1f);
        rb.constraints = RigidbodyConstraints.None;
    }
}
