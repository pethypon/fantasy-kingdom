using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BattleSystem : MonoBehaviour
{
    public Status target;
    public Status AttackSide;
    public TurnGenerater turngenerater;
    public void DamageGenerater(TurnGenerater turngenerater)
    {
        this.turngenerater = turngenerater;
        if (target != null)
        {
            if (turngenerater.SelectUnit != null)
            {
                AttackSide = turngenerater.SelectUnit;
                if(AttackSide.kind == Kind.Archer)
                {

                }

                if (AttackSide.kind == Kind.Magic)
                {

                }

                if (AttackSide.kind == Kind.Assassin)
                {

                }

                if(AttackSide.kind == Kind.Guardian)
                {

                }

                if(AttackSide.kind == Kind.Crossbow)
                {

                }

                
            }
        }
    }

    public void targetside()
    {
        if (target.kind == Kind.Knight)
        {

        }
        
        
    }
}
