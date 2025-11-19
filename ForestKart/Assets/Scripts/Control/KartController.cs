using UnityEngine;
using System;
using System.Collections;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using Unity.Netcode;

public class KartController : NetworkBehaviour
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
    public bool controlsEnabled = false;
    private bool hasBeenEnabled = false;
    [Header("Stability")]
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.5f, 0f);
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass += centerOfMassOffset;
        if (taillight != null)
        {
            taillight.GetComponent<Renderer>().material.DisableKeyword("_EMISSION");
        }
        
        if (!hasBeenEnabled)
        {
            controlsEnabled = false;
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (hasBeenEnabled && !controlsEnabled)
        {
            controlsEnabled = true;
        }
        
        if (IsOwner)
        {
            StartCoroutine(SetupPlayerInputDelayed());
        }
    }
    
    private System.Collections.IEnumerator SetupPlayerInputDelayed()
    {
        yield return new WaitForSeconds(0.1f);
        
        UnityEngine.InputSystem.PlayerInput playerInput = GetComponent<UnityEngine.InputSystem.PlayerInput>();
        if (playerInput != null)
        {
            playerInput.enabled = true;
            playerInput.ActivateInput();
        }
    }
    
    private void Update()
    {
        if (hasBeenEnabled && !controlsEnabled)
        {
            controlsEnabled = true;
        }
        
        if (!controlsEnabled) return;
        
        Drive(gas, brake,steer,drift);
        AddDownForce(); 
    }
    
    public void EnableControls()
    {
        hasBeenEnabled = true;
        controlsEnabled = true;
        
        if (IsOwner)
        {
            StartCoroutine(ActivateInputAfterDelay());
        }
    }
    
    private System.Collections.IEnumerator ActivateInputAfterDelay()
    {
        yield return new WaitForSeconds(0.1f);
        
        UnityEngine.InputSystem.PlayerInput playerInput = GetComponent<UnityEngine.InputSystem.PlayerInput>();
        if (playerInput != null)
        {
            if (!playerInput.enabled)
            {
                playerInput.enabled = true;
            }
            playerInput.ActivateInput();
            
            if (playerInput.currentActionMap == null)
            {
                var actions = playerInput.actions;
                if (actions != null)
                {
                    var driveMap = actions.FindActionMap("Drive");
                    if (driveMap != null)
                    {
                        playerInput.SwitchCurrentActionMap("Drive");
                    }
                }
            }
        }
    }
    
    public void DisableControls()
    {
        hasBeenEnabled = false;
        controlsEnabled = false;
        gas = 0f;
        brake = 0f;
        steer = Vector2.zero;
        drift = Vector2.zero;
    }
    public void OnAccelerate(InputValue value)
    {
        if (!IsOwner || !controlsEnabled) return;
        
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
        if (!IsOwner || !controlsEnabled) return;
        
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
        if (!IsOwner || !controlsEnabled) return;
        
        steer = value.Get<Vector2>();
    }
    public void OnReverse(InputValue value)
    {
        if (!IsOwner || !controlsEnabled) return;
        
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
        if (!IsOwner || !controlsEnabled) return;
        
        transform.rotation = new Quaternion(0, 0, 0, 0);
    }
    public void OnDrift(InputValue value)
    {
        if (!IsOwner || !controlsEnabled) return;
        
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
