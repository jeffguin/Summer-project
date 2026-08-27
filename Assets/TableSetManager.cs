using UnityEngine;
using System.Collections;
using System.Collections.Generic;


public class TableSetManager : MonoBehaviour
{
    [Header("Table Prop Sets")]
    public List<GameObject> propSets = new List<GameObject>();

    [Header("Transition Blockers")]
    public List<FadeObject> viewBlockers = new List<FadeObject>();

    [Header("Transition Settings")]
    public float transitionDelay = 0.5f;


    private int currentSet = -1;

    private bool switching = false;



    public void LoadSet(int index)
    {
        if (switching)
            return;


        if(index < 0 || index >= propSets.Count)
        {
            Debug.LogWarning("Invalid prop set index");
            return;
        }


        StartCoroutine(SwitchSet(index));
    }



    IEnumerator SwitchSet(int index)
    {
        switching = true;


        // Hide transition
        foreach(FadeObject blocker in viewBlockers)
        {
            blocker.FadeIn();
        }


        yield return new WaitForSeconds(transitionDelay);



        // Remove previous set
        ClearSets();



        // Activate selected set
        GameObject selectedSet = propSets[index];

        selectedSet.SetActive(true);



        // Reset all objects inside
        ResettableObject[] objects =
            selectedSet.GetComponentsInChildren<ResettableObject>(true);


        foreach(ResettableObject obj in objects)
        {
            obj.ResetObject();
        }


        currentSet = index;



        yield return new WaitForSeconds(transitionDelay);



        // Reveal transition
        foreach(FadeObject blocker in viewBlockers)
        {
            blocker.FadeOut();
        }


        switching = false;
    }




    void ClearSets()
    {
        foreach(GameObject set in propSets)
        {
            set.SetActive(false);
        }
    }



    // Optional manual clear button
    public void ClearTable()
    {
        ClearSets();

        currentSet = -1;


        foreach(FadeObject blocker in viewBlockers)
        {
            blocker.FadeOut();
        }
    }
}