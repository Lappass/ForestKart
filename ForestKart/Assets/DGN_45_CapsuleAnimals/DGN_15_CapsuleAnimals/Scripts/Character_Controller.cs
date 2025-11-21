using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DGN_CapsuleCharacters
{
    public class Character_Controller : MonoBehaviour
    {
        [HideInInspector] public string _AnimationName;
        public void SetAnimation()
        {
            GetComponent<Animator>().Play(_AnimationName);
        }
    }
}
