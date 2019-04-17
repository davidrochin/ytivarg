﻿using Photon.Pun;
using System.Collections.Generic;
using UnityEngine;

namespace Unimotion {
    public class Player : MonoBehaviourPun {

        Character character;
        CharacterMotor motor;
        Animator animator;
        GameCamera playerCamera;

        public float minTargetDistance = 5f;
        public InputType inputType;

        public CameraSocket cameraSocket;
        public Target target;

        private float targetAngle = 25f;

        void Awake() {
            DontDestroyOnLoad(this);
            motor = GetComponent<CharacterMotor>();
            character = GetComponent<Character>();
            playerCamera = Camera.main.GetComponent<GameCamera>();

            // Create the camera socket
            cameraSocket = new GameObject("Camera Socket").AddComponent<CameraSocket>();
            cameraSocket.gameObject.name = "Camera Socket";
            DontDestroyOnLoad(cameraSocket.gameObject);
        }

        private void Start() {
            animator = GetComponent<CharacterMotor>().animator;

            if (!photonView.IsMine && PhotonNetwork.IsConnected && GetComponent<CharacterMotor>() != null) {
                Destroy(GetComponent<CharacterMotor>());
            }

            motor.OnFrameFinish += PositionSocket;

        }

        void LateUpdate() {

            

            float inputMagnitude = GetInputMagnitude();
            Vector3 inputVector = GetInputVector();

            // Movement
            if (inputMagnitude > 0.05f) {
                motor.Walk(inputVector * inputMagnitude * (Input.GetButton("Circle") ? 1.5f : 1f) * (Input.GetKey(KeyCode.LeftAlt) ? 0.5f : 1f));
                motor.TurnTowards(inputVector);
            }

            // Jumping
            if (Input.GetButtonDown("Cross")) {
                motor.Jump();
            }

            if (Input.GetKeyDown(KeyCode.R)) {
                motor.velocity = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized * Random.Range(20f, 40f) + Vector3.up * 10f;
            }

            if (Input.GetKeyDown(KeyCode.F)) {
                motor.AddForce(transform.forward * 500f);
            }

            // Targeting
            if (Input.GetButtonDown("R3")) {
                if (target == null) {
                    List<Target> targetables = new List<Target>(FindObjectsOfType<Target>());
                    targetables.Sort(delegate (Target x, Target y) {
                        return Vector3.Distance(this.transform.position, x.transform.position).CompareTo(Vector3.Distance(this.transform.position, y.transform.position));
                    });

                    targetables = targetables.FindAll(delegate (Target target) {
                        return Vector3.Distance(transform.position, target.transform.position) <= minTargetDistance;
                    });

                    if (targetables.Count > 0) {
                        target = targetables[0];
                    }
                } else {
                    target = null;
                }

            }

            // Attacking
            if (Input.GetButtonDown("R1") && !character.IsBlocked()) {
                animator.CrossFadeInFixedTime("Attack", 0.2f);
            }

            // Evading
            if (Input.GetButtonDown("Circle") && !character.IsBlocked()) {
                switch (GetInputDirection()) {
                    case Direction.Left:
                        animator.CrossFadeInFixedTime("Dodge Left", 0.2f);
                        break;
                    case Direction.Right:
                        animator.CrossFadeInFixedTime("Dodge Right", 0.2f);
                        break;
                    case Direction.Up:
                        animator.CrossFadeInFixedTime("Dodge Front", 0.2f);
                        break;
                    case Direction.Down:
                    case Direction.None:
                        animator.CrossFadeInFixedTime("Dodge Back", 0.2f);
                        break;
                }

            }

        }

        public Direction GetInputDirection() {
            float threshold = 0.4f;
            Vector3 input = GetInputVector();

            float forward = Vector3.Dot(input, motor.transform.forward);
            float right = Vector3.Dot(input, motor.transform.right);

            if (Mathf.Abs(forward) > threshold || Mathf.Abs(right) > threshold) {
                if (Mathf.Abs(forward) > Mathf.Abs(right)) {
                    return forward > 0f ? Direction.Up : Direction.Down;
                } else {
                    return right > 0f ? Direction.Right : Direction.Left;
                }
            }
            return Direction.None;
        }

        Vector3 GetInputVector() {
            Vector3 input = Vector3.zero;

            if (inputType == InputType.Normal) {
                input = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));

            } else if (inputType == InputType.Raw) {
                input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            }


            //Transformar la direccion para que sea relativa a la camara.
            Quaternion tempQ = Quaternion.Euler(0f, Camera.main.transform.eulerAngles.y, 0f);
            Vector3 transDirection = Camera.main.transform.rotation * input;
            transDirection = Quaternion.FromToRotation(Camera.main.transform.up, -motor.GetGravity().normalized) * transDirection;

            //Hacer que el Vector no apunte hacia arriba.
            //transDirection = new Vector3(transDirection.x, 0f, transDirection.z).normalized;
            finalMovementVector = transDirection;
            return transDirection.normalized;
        }

        float GetInputMagnitude() {
            Vector3 input = Vector3.zero;

            // Get Input from standard Input methods
            if (inputType == InputType.Normal) { input = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical")); } else if (inputType == InputType.Raw) { input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")); }

            // Clamp magnitude to 1
            return Vector3.ClampMagnitude(input, 1f).magnitude;
        }

        public void PositionSocket() {

            // Get the real target position (add offset)
            Vector3 realTarget = motor.transform.position + new Vector3(0f, 1f, 0f);

            // Make a vector from mouse/joystick movement
            Vector3 input = Vector3.zero;
            if (!Application.isMobilePlatform) {
                input = new Vector3(Input.GetAxis("Mouse X"), -Input.GetAxis("Mouse Y"), 0f);
            }
            input = input + new Vector3(Input.GetAxis("Camera Horizontal") * 60f * Time.deltaTime, Input.GetAxis("Camera Vertical") * 60f * Time.deltaTime, 0f);

            // Rotate the Camera
            float orbitSpeed = 2.5f;
            cameraSocket.transform.RotateAround(motor.transform.position, -motor.GetGravity().normalized, input.x * orbitSpeed);
            cameraSocket.transform.RotateAround(motor.transform.position, cameraSocket.transform.right, input.y * orbitSpeed);

            // Before calculating desired distance, see if there is any obstacles to avoid
            float distance = 3f;
            RaycastHit hit;
            bool didHit = Physics.Raycast(realTarget, transform.forward * -1f, out hit, distance, LayerMask.GetMask(new string[] { "Default" }), QueryTriggerInteraction.Ignore);

            float maxDistance = 0f;
            Vector3 addition = Vector3.zero;

            if (hit.collider != null) {
                maxDistance = hit.distance;
                addition = hit.normal;
            } else {
                maxDistance = distance;
            }

            // Put the Camera around the player
            Vector3 desiredPosition = realTarget - cameraSocket.transform.forward * (maxDistance - 0.1f);
            cameraSocket.transform.position = desiredPosition;

            // If there is a combat target
            if (target != null) {

                // Move the camera so it accomodates the player right behind the target
                Quaternion tmp = Quaternion.LookRotation((target.transform.position - transform.position).normalized, -motor.GetGravity().normalized);
                cameraSocket.transform.rotation = Quaternion.Lerp(cameraSocket.transform.rotation, tmp, 2 * Time.deltaTime);

                // Fix the camera at certain angle
                cameraSocket.transform.localEulerAngles = new Vector3(targetAngle, cameraSocket.transform.localEulerAngles.y, cameraSocket.transform.localEulerAngles.z);

                // Look at the target
                Camera.main.transform.rotation = Quaternion.LookRotation((target.transform.position - Camera.main.transform.position).normalized, -motor.GetGravity().normalized);
                motor.TurnTowards((target.transform.position - transform.position), CharacterMotor.TurnBehaviour.Instant);
            }

            // Correct camera rotation
            Camera.main.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward, -motor.GetGravity().normalized);

        }

        public static void RefreshInstance(ref Player player, GameObject prefab) {

            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;

            if (player != null) {
                position = player.transform.position;
                rotation = player.transform.rotation;
                PhotonNetwork.Destroy(player.gameObject);
            }

            player = PhotonNetwork.Instantiate(prefab.gameObject.name, position, rotation).GetComponent<Player>();
        }

        Vector3 finalMovementVector;
        private void OnDrawGizmosSelected() {
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position, finalMovementVector);
        }

    }

    public enum Direction { Up, Right, Down, Left, None }
    public enum InputType { Normal, Raw }
}


