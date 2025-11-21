using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DGN_CapsuleCharacters
{
    public class Button_Functions45 : MonoBehaviour
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
        #region V1 Locations
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
        #endregion
        #region V2 Locations
        public void SetPanther()
        {
            _Placer.localPosition = new Vector3(-300f, 0f, 0f);
        }
        public void SetHorse()
        {
            _Placer.localPosition = new Vector3(-320f, 0f, 0f);
        }
        public void SetTiger()
        {
            _Placer.localPosition = new Vector3(-340f, 0f, 0f);
        }
        public void SetPolar_Bear()
        {
            _Placer.localPosition = new Vector3(-360f, 0f, 0f);

        }
        public void SetRaccoon()
        {
            _Placer.localPosition = new Vector3(-380f, 0f, 0f);
        }
        public void SetMonkey()
        {
            _Placer.localPosition = new Vector3(-400f, 0f, 0f);
        }
        public void SetHippo()
        {
            _Placer.localPosition = new Vector3(-420f, 0f, 0f);
        }
        public void SetMoose()
        {
            _Placer.localPosition = new Vector3(-440f, 0f, 0f);
        }
        public void SetZebra()
        {
            _Placer.localPosition = new Vector3(-460f, 0f, 0f);
        }
        public void SetLeopard()
        {
            _Placer.localPosition = new Vector3(-480f, 0f, 0f);
        }
        public void SetBuffalo()
        {
            _Placer.localPosition = new Vector3(-500f, 0f, 0f);
        }
        public void SetDog2()
        {
            _Placer.localPosition = new Vector3(-520f, 0f, 0f);
        }
        public void SetElephant()
        {
            _Placer.localPosition = new Vector3(-540f, 0f, 0f);
        }
        public void SetGorilla()
        {
            _Placer.localPosition = new Vector3(-560f, 0f, 0f);
        }
        public void SetRhino()
        {
            _Placer.localPosition = new Vector3(-580f, 0f, 0f);
        }
        #endregion
        #region V3 Locations
        public void SetBoar()
        {
            _Placer.localPosition = new Vector3(-600f, 0f, 0f);
        }
        public void SetOtter()
        {
            _Placer.localPosition = new Vector3(-620f, 0f, 0f);
        }
        public void SetLion()
        {
            _Placer.localPosition = new Vector3(-640f, 0f, 0f);
        }
        public void SetGrife()
        {
            _Placer.localPosition = new Vector3(-660f, 0f, 0f);

        }
        public void SetSiamese()
        {
            _Placer.localPosition = new Vector3(-680f, 0f, 0f);
        }
        public void SetEagle()
        {
            _Placer.localPosition = new Vector3(-700f, 0f, 0f);
        }
        public void SetHyena()
        {
            _Placer.localPosition = new Vector3(-720f, 0f, 0f);
        }
        public void SetDonkey()
        {
            _Placer.localPosition = new Vector3(-740f, 0f, 0f);
        }
        public void SetLemur()
        {
            _Placer.localPosition = new Vector3(-760f, 0f, 0f);
        }
        public void SetSheep()
        {
            _Placer.localPosition = new Vector3(-780f, 0f, 0f);
        }
        public void SetShiba()
        {
            _Placer.localPosition = new Vector3(-800f, 0f, 0f);
        }
        public void SetCrocodile()
        {
            _Placer.localPosition = new Vector3(-820f, 0f, 0f);
        }
        public void SetAntelope()
        {
            _Placer.localPosition = new Vector3(-840f, 0f, 0f);
        }
        public void SetWolf()
        {
            _Placer.localPosition = new Vector3(-860f, 0f, 0f);
        }
        public void SetSloth()
        {
            _Placer.localPosition = new Vector3(-880f, 0f, 0f);
        }
        #endregion
    }


}


