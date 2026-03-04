using UnityEngine;

public class GameEndState : StateCore
{
    private TurnGenerater _turn;
    private GameResult _result;

    public GameEndState(TurnGenerater turn, GameResult result)
    {
        _turn = turn;
        _result = result;
    }

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ ƒXƒe[ƒgŠJŽnFŒ‹‰Ê•\Ž¦E‘€ì’âŽ~ „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
    public void Entry()
    {
        Debug.Log($"[GameEnd] ƒQ[ƒ€I—¹ „Ÿ„Ÿ {_result}");

        // TODO: ƒŠƒUƒ‹ƒgUI•\Ž¦
        // —á: UIManager.Instance.ShowResult(_result);
    }

    // „Ÿ„Ÿ„Ÿ –ˆƒtƒŒ[ƒ€F‰½‚à‚µ‚È‚¢i“ü—Í‚ðŽó‚¯•t‚¯‚È‚¢j „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
    public void Update()
    {
        // ‘€ì’âŽ~ó‘Ô
        // •K—v‚È‚çƒŠƒgƒ‰ƒC^ƒ^ƒCƒgƒ‹‚Ö–ß‚éƒ{ƒ^ƒ“‚¾‚¯Žó‚¯•t‚¯‚é
        // —á:
        // if (_turn.RetryDown) SceneManager.LoadScene("Game");
        // if (_turn.QuitDown)  SceneManager.LoadScene("Title");
=======
    // â”€â”€â”€ ã‚¹ãƒ†ãƒ¼ãƒˆé–‹å§‹ï¼šçµæžœè¡¨ç¤ºãƒ»æ“ä½œåœæ­¢ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public void Entry()
    {
        Debug.Log($"[GameEnd] ã‚²ãƒ¼ãƒ çµ‚äº† â”€â”€ {_result}");

        // TODO: ãƒªã‚¶ãƒ«ãƒˆUIè¡¨ç¤º
        // ä¾‹: UIManager.Instance.ShowResult(_result);
    }

    // â”€â”€â”€ æ¯Žãƒ•ãƒ¬ãƒ¼ãƒ ï¼šä½•ã‚‚ã—ãªã„ï¼ˆå…¥åŠ›ã‚’å—ã‘ä»˜ã‘ãªã„ï¼‰ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public void Update()
    {
        // æ“ä½œåœæ­¢çŠ¶æ…‹
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    }

    public void Exit() { }
}
