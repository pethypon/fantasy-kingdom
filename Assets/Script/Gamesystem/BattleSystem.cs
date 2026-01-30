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
        //NoneˆÈŠO‚Í‹î‚²‚Æ‚Ì‹ŠE‚ªŠ®¬‚µ‚½‚ç§ì
        if (target != null)
        {
            if (turngenerater.SelectUnit != null)
            {
                AttackSide = turngenerater.SelectUnit;
                switch (AttackSide.passiveskill)
                {
                    case PassiveSkill.HunterEyes:

                        break;
                    case PassiveSkill.Destroyer:

                        break;

                    case PassiveSkill.Assassination:
                        break;

                    case PassiveSkill.Sniper:

                        break;

                    case PassiveSkill.None:
                        int damage = (AttackSide.ATK - target.DEF);
                        damage = Mathf.Max(0,damage);
                        target.HP -= damage;
                        target.HP = Mathf.Max(0, target.HP);
                        
                        break;
                }

               
                
            }
        }
    }

    public void SideDefender()
    {

        switch (target.passiveskill)
        {
            case PassiveSkill.Impregnable:

                break;
            case PassiveSkill.None:

                break;

           
        }
    }

}
