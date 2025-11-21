using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DGN_CapsuleCharacters
{
    public class Button_Functions : MonoBehaviour
    {
        public GameObject[] _Characters;
        public GameObject _Ground;
        public Transform _Placer;
        #region Animations Sets
        public void SetAnimLookAround()
        {
            foreach (GameObject obj in _Characters)
            {
                obj.GetComponent<Animator>().Play("Test_LookingAround");
            }
            _Ground.GetComponent<Rotater>().rotationSpeed = 0f;
            _Ground.GetComponent<Transform>().rotation = Quaternion.Euler(0f, 0f, 0f);
        }
        public void SetAnimWalking()
        {
            foreach (GameObject obj in _Characters)
            {
                obj.GetComponent<Animator>().Play("Test_Walking");
            }
            _Ground.GetComponent<Transform>().rotation = Quaternion.Euler(0f, 0f, 0f);
            _Ground.GetComponent<Rotater>().rotationSpeed = 50f;
        }

        #endregion
        #region Locations Sets
        public void SetBull()
        {
            _Placer.localPosition = new Vector3(0f, 0f, 0f);
        }
        public void SetDeer()
        {
            _Placer.localPosition = new Vector3(-120f, 0f, 0f);
        }
        public void SetBear()
        {
            _Placer.localPosition = new Vector3(-20f, 0f, 0f);
        }
        public void SetBeaver()
        {
            _Placer.localPosition = new Vector3(-40f, 0f, 0f);

        }
        public void SetRam()
        {
            _Placer.localPosition = new Vector3(-60f, 0f, 0f);
        }
        public void SetCat()
        {
            _Placer.localPosition = new Vector3(-80f, 0f, 0f);
        }
        public void SetPig()
        {
            _Placer.localPosition = new Vector3(-220f, 0f, 0f);
        }
        public void SetRabbit()
        {
            _Placer.localPosition = new Vector3(-240f, 0f, 0f);
        }
        public void SetDog()
        {
            _Placer.localPosition = new Vector3(-140f, 0f, 0f);
        }
        public void SetCow()
        {
            _Placer.localPosition = new Vector3(-100f, 0f, 0f);
        }
        public void SetFox()
        {
            _Placer.localPosition = new Vector3(-160f, 0f, 0f);
        }
        public void SetKoala()
        {
            _Placer.localPosition = new Vector3(-180f, 0f, 0f);
        }
        public void SetMouse()
        {
            _Placer.localPosition = new Vector3(-200f, 0f, 0f);
        }
        public void SetPanda()
        {
            _Placer.localPosition = new Vector3(-260f, 0f, 0f);
        }
        public void SetSquirrel()
        {
            _Placer.localPosition = new Vector3(-280f, 0f, 0f);
        }
    }
    #endregion
}
