using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class CharController : MonoBehaviour
{
    public float baseMovementSpeed = 10f;
    public float rotationSpeed = 100f;
    public float jumpForce = 5f;
    private bool isGrounded; // To check if the player is on the ground
    public float maxHorizontalLimit = 5f; // Maximum distance character can move horizontally
    private float originalXPosition; // To store the original x-position of the character
    private float originalAnimatedPlayerYPosition; // To reset y later on if it is modified by animations interruptions
    private float speedIncreaseFactor = 0.0025f; // Factor to increase according to distance covered by player in Z
    private Rigidbody rigidBody;
    public SpawnManager spawnManager; // Handles spawning roads and other objects
    public GameManager gameManager; // Handles game data like distance covered and collected coins
    public GameObject animatedPlayer; // The dog itself to reset its y
    public Animator animator; // Handles animations
    public ParticleSystem dirtParticleSystem; // Handles dirt particles
    public GameObject coinParticleSystemPrefab; // To add a coin collection particle animation when hitting a coin
    private GameObject lastTriggeredSpawn = null; // To not trigger same roadspawners twice in a row
    private bool canMove = true; // Variable to control ability to move to be triggered on death

    // Use this for initialization
    void Start()
    {
        originalXPosition = transform.position.x;
        originalAnimatedPlayerYPosition = animatedPlayer.transform.position.y;
        rigidBody = GetComponent<Rigidbody>();

    }

    // Update is called once per frame
    void Update()
    {
        // Don't update if dead
        if (!canMove) return;

        // Reset player y position because interrupting the running animation can result in undesired elevation
        // (running animation has a keyframe in the middle with elevated position in y)
        if (Mathf.Abs(animatedPlayer.transform.position.y - originalAnimatedPlayerYPosition) >= 0.1f && isGrounded)
        {
            Debug.Log("fixed");
            Vector3 position = animatedPlayer.transform.position;
            position.y = originalAnimatedPlayerYPosition;
            animatedPlayer.transform.position = position;
        }


        // Speed increases progressively to get more difficult (max possible increment is 7 to not get too fast)
        float dynamicSpeed = Mathf.Min(baseMovementSpeed + (transform.position.z * speedIncreaseFactor), baseMovementSpeed + 7);

        float hMovement = Input.GetAxis("Horizontal") * dynamicSpeed * 0.8f;
        float vMovement = Input.GetAxis("Vertical") * dynamicSpeed;

        // If there is significant horizontal movement, reduce the vertical speed
        if (Mathf.Abs(hMovement) > 0.1f) // 
        {
            vMovement *= 0.85f; // Reduce vertical speed when turning
        }

        // Check if there's horizontal or vertical movement to consider the character as running
        bool isRunning = Mathf.Abs(hMovement) > 0 || Mathf.Abs(vMovement) > 0;
        animator.SetBool("isRunning", isRunning); // Update the animator parameter


        //Prevent backward movement
        if (vMovement < 0)
        {
            vMovement = 0;
            if (Mathf.Abs(hMovement) < 0.1f) animator.SetBool("isRunning", false); // Update the animator parameter as well if there is no horizontal motion

        }

        // Apply movement
        Vector3 desiredPosition = transform.position + new Vector3(hMovement, 0, vMovement) * Time.deltaTime;

        // Clamp the horizontal position
        desiredPosition.x = Mathf.Clamp(desiredPosition.x, originalXPosition - maxHorizontalLimit, originalXPosition + maxHorizontalLimit);
        transform.position = desiredPosition;

        // Calculate target rotation based on movement
        float targetRotation = 0f;
        if (Mathf.Abs(hMovement) > 0.1f) // Only rotates when there is at least a little bit of horizontal movement 
        {
            float maxRotation = vMovement > 0 ? 20f : 70f; // If moving forward, then the rotation angle is less
            targetRotation = maxRotation * Mathf.Sign(hMovement); // Multiplying by the sign to get the proper direction
        }

        // Calculate smooth rotation
        float currentYRotation = transform.eulerAngles.y;
        float rotationStep = Mathf.SmoothStep(0f, 1f, Time.deltaTime * rotationSpeed);  // Use SmoothStep for a smoother transition
        float newYRotation = Mathf.LerpAngle(currentYRotation, targetRotation, rotationStep);

        transform.rotation = Quaternion.Euler(0, newYRotation, 0);


        // Jumping
        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            isGrounded = false; // The player is no longer on the ground
            rigidBody.AddForce(new Vector3(0, jumpForce, 0), ForceMode.Impulse);
            animator.SetBool("isJumping", true); // Notify the animator about the jump
            dirtParticleSystem.Stop(); // Turn off dirt particles while jumping
        }

    }


    // Collision with isTrigger objects (coins, road spawner trigger)
    private void OnTriggerEnter(Collider collision)
    {
        // Check if a player hit a spawn trigger collider
        if (collision.gameObject.tag == "SpawnTrigger")
        {
            // Check if this is the same object as the last one triggered (to avoid bugs resulting from hitting the same spawn trigger twice)
            if (lastTriggeredSpawn != collision.gameObject)
            {
                spawnManager.SpawnTriggerEntered(); // Spawn another block
                lastTriggeredSpawn = collision.gameObject; // Update the last triggered spawn
            }
        }

        if (collision.gameObject.tag == "Coin")
        {

            // Instantiate the particle system at the coin's position
            GameObject particleSystemObject = Instantiate(coinParticleSystemPrefab, collision.transform.position, Quaternion.identity);

            // Get the compononent
            ParticleSystem particleSystem = particleSystemObject.GetComponent<ParticleSystem>();

            // Destroy it after its done
            Destroy(particleSystemObject, particleSystem.main.duration + particleSystem.main.startLifetime.constantMax);

            // Add the coin to the game manager
            gameManager.AddCoin();

            // Destroy the coin
            Destroy(collision.gameObject);


        }
    }

    // Collision with normal objects like ground and obstacles
    private void OnCollisionEnter(Collision collision)
    {

        // Check if the player has hit the ground
        if (collision.gameObject.tag == "Ground")
        {
            dirtParticleSystem.Play(); // Turn on dirt particles while on ground
            isGrounded = true; // The player is now on the ground
            animator.SetBool("isJumping", false); // Ensure the jump parameter is reset
        }

        // Punish player for hitting obstacles or enemies
        if ((collision.gameObject.tag == "Obstacle" && transform.position.z < collision.transform.position.z) || collision.gameObject.tag == "Instantiatable")
        {
            // Backward force equivelant to mass of object the player collides with, max possible is 50 to prevent incredibly large forces
            float backwardForce = Mathf.Min(collision.rigidbody.mass, 50);
            // Apply the backward force
            Vector3 backwardVector = -transform.forward * backwardForce;
            rigidBody.AddForce(backwardVector, ForceMode.Impulse);

            // Hitting heavy obstacles (that are in front of you) or enemies kills
            if (collision.gameObject.tag == "Instantiatable" || (collision.collider.attachedRigidbody.mass >= 100))
            {
                // Disable Rigidbody physics to not interefere with death animation
                rigidBody.velocity = Vector3.zero; // Reset velocity to stop the GameObject from moving
                rigidBody.angularVelocity = Vector3.zero; // Reset angular velocity to stop rotations

                // Disable movement
                canMove = false;

                // Play dog dying animation
                animator.SetBool("isDead", true);

                // Reset this object
                lastTriggeredSpawn = null;

                // Wait a bit (for the animation to show) then restart level
                StartCoroutine(DelayMenuLoad(1.0f)); // 1 second delay
            }
        }

    }


    // Coroutines to delay events

    // Delays loading the menu so that the player death animation can have time to play
    IEnumerator DelayMenuLoad(float delay)
    {
        // Wait for the specified delay
        yield return new WaitForSeconds(delay);

        // Display game over menu
        gameManager.GameOver();
    }

}