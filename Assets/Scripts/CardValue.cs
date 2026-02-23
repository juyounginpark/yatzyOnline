using UnityEngine;

public enum CardType
{
    Attack,
    Critical,
    Heal
}

public class CardValue : MonoBehaviour
{
    public int value;
    public bool isJoker;
    public CardType cardType = CardType.Attack;
    public int poolIndex;
}
