using UnityEngine;

public enum CardType
{
    Attack,
    Heal
}

public class CardValue : MonoBehaviour
{
    public int value;
    public bool isJoker;
    public CardType cardType = CardType.Attack;
    public int poolIndex;
}
