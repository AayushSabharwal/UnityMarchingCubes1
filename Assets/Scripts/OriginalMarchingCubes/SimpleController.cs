using UnityEngine;

public class SimpleController : MonoBehaviour
{
    [SerializeField]
    private float moveSpeed = 7f;

    private float _h;
    private float _v;

    private void Update()
    {
        _h = Input.GetAxis("Horizontal") * moveSpeed * Time.deltaTime;
        _v = Input.GetAxis("Vertical") * moveSpeed * Time.deltaTime;
        transform.Translate(_v, 0f, _h);
    }
}