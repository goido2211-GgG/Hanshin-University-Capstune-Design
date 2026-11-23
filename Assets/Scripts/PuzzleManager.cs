using UnityEngine;
using System.Collections.Generic;

public class PuzzleManager : MonoBehaviour
{
    [Header("Puzzle Settings")]
    [Tooltip("정답 순서 (레버의 ID를 순서대로 입력하세요. 예: 1, 3, 2, 4)")]
    [SerializeField] private List<int> correctSequence = new List<int> { 1, 2, 3, 4 };

    private List<int> playerInput = new List<int>();

    [Header("References")]
    [Tooltip("정답을 맞췄을 때 열릴 문 오브젝트")]
    [SerializeField] private GameObject door;

    // 레버가 당겨졌을 때 호출되는 함수
    public void SubmitLever(int leverID)
    {
        playerInput.Add(leverID);
        Debug.Log($"레버 입력됨: {leverID} | 현재 입력 개수: {playerInput.Count}/{correctSequence.Count}");

        int currentIndex = playerInput.Count - 1;

        // 1. 입력한 순서가 정답 순서와 틀린 경우 -> 즉시 초기화
        if (playerInput[currentIndex] != correctSequence[currentIndex])
        {
            Debug.Log("틀렸습니다! 입력 순서가 초기화됩니다.");
            ResetPuzzle();
            return;
        }

        // 2. 모든 순서를 올바르게 다 맞춘 경우 -> 성공
        if (playerInput.Count == correctSequence.Count)
        {
            SuccessPuzzle();
        }
    }

    private void ResetPuzzle()
    {
        playerInput.Clear();

        // Unity 6 최신 문법으로 변경
        LeverItem[] allLevers = Object.FindObjectsByType<LeverItem>(FindObjectsSortMode.None);
        foreach (var lever in allLevers)
        {
            lever.ResetLever();
        }
    }

    private void SuccessPuzzle()
    {
        Debug.Log("퍼즐 정답! 문이 열립니다.");

        if (door != null)
        {
            // Door 오브젝트에 붙은 DoorController 스크립트를 가져와서 OpenDoor 함수 실행
            DoorController doorScript = door.GetComponent<DoorController>();
            if (doorScript != null)
            {
                doorScript.OpenDoor();
            }
            else
            {
                // 혹시 스크립트를 깜빡했다면 기존처럼 비활성화 방어 코드
                door.SetActive(false);
            }
        }
    }
}