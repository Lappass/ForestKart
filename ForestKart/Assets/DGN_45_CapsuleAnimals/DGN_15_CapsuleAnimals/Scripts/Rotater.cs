using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DGN_CapsuleCharacters
{
    public class Rotater : MonoBehaviour
    {
        public float rotationSpeed = 0f;

        void Update()
        {
            transform.Rotate(Vector3.down * rotationSpeed * Time.deltaTime);
        }
    }


}
