using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundEffects : MonoBehaviour
{
    private static SoundEffects _instance;
    public static SoundEffects GetInstance()
    {
        return _instance;
    }

    [SerializeField]
    private FMODUnity.StudioEventEmitter[] eventEmitters; 

    [SerializeField]
    [Range(0f, 1f)]
    public float coverRatio;

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
        }
        else {
            DestroyImmediate(this);
        }

        DontDestroyOnLoad(this);
    }

    // Start is called before the first frame update
    private void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        FMODUnity.RuntimeManager.StudioSystem.setParameterByName("CoverRatio", coverRatio);
    }
}
