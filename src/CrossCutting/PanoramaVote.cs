using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Timers;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace cs2_rockthevote
{
    public enum CastVote
    {
        VOTE_NOTINCLUDED = -1,
        VOTE_OPTION1,  // Use this for Yes
        VOTE_OPTION2,  // Use this for No
        VOTE_OPTION3,
        VOTE_OPTION4,
        VOTE_OPTION5,
        VOTE_UNCAST = 5
    }

    public enum YesNoVoteEndReason
    {
        VoteEnd_AllVotes,  // All possible votes were cast
        VoteEnd_TimeUp,    // Time ran out
        VoteEnd_Cancelled  // The vote got cancelled
    }

    public enum YesNoVoteAction
    {
        VoteAction_Start,  // nothing passed
        VoteAction_Vote,   // param1 = client slot, param2 = choice (VOTE_OPTION1 = yes, VOTE_OPTION2 = no)
        VoteAction_End     // param1 = YesNoVoteEndReason reason why the vote ended
    }

    public class YesNoVoteInfo
    {
        public int num_votes;                // Number of votes tallied in total
        public int yes_votes;                // Number of votes for yes
        public int no_votes;                 // Number of votes for no
        public int num_clients;              // Number of clients who could vote
        //public int[,] clientInfo = new int[MAXPLAYERS, 2];  // Client voting info, user VOTEINFO_CLIENT_ defines. Anything >= [num_clients] is VOTE_NOTINCLUDED, VOTE_UNCAST = client didn't vote
        public Dictionary<int, (int, int)> clientInfo = new Dictionary<int, (int, int)>();
    }

    public class VoteConstants
    {
        public const int VOTE_CALLER_SERVER = 99;
        public const int VOTE_NOTINCLUDED = -1;
        public const int VOTE_UNCAST = 5;
        public const int MAXPLAYERS = 64;
    }

    public delegate bool YesNoVoteResult(YesNoVoteInfo info);
    public delegate void YesNoVoteHandler(YesNoVoteAction action, int param1, int param2);

    public class PanoramaVote(ILogger<PanoramaVote> logger) : IPluginDependency<Plugin, Config>
    {
        private readonly ILogger<PanoramaVote> _logger = logger;
        private int m_iVoteCount = 0;
        private bool m_bIsVoteInProgress = false;
        private YesNoVoteHandler? m_VoteHandler = null;
        private YesNoVoteResult? m_VoteResult = null;
        private int m_iVoterCount;
        private int[] m_iVoters = new int[VoteConstants.MAXPLAYERS];
        private int m_iCurrentVoteCaller;
        private string m_szCurrentVoteTitle = string.Empty;
        private string m_szCurrentVoteDetailStr = string.Empty;
        public CVoteController? VoteController { get; private set; } = null;
        private RecipientFilter CurrentVotefilter = new RecipientFilter();
        private Timer? m_VoteTimer = null;
        private ILogger _debugLogger = NullLogger.Instance;

        public void OnConfigParsed(Config config) =>
            _debugLogger = DebugLog.For(_logger, config);

        /// Resets the vote state, clearing any ongoing vote information.
        public void Reset(CVoteController? voteController = null)
        {
            m_VoteTimer?.Kill();
            m_VoteTimer = null;
            m_bIsVoteInProgress = false;
            m_VoteHandler = null;
            m_VoteResult = null;
            m_szCurrentVoteTitle = string.Empty;
            m_szCurrentVoteDetailStr = string.Empty;
            CurrentVotefilter = new RecipientFilter();

            if(voteController != null)
            {
                for (int i = 0; i < VoteConstants.MAXPLAYERS; i ++)
                {
                    voteController.VotesCast[i] = (int)CastVote.VOTE_UNCAST;
                }

                voteController.VoteOptionCount[0] = 0;
                voteController.VoteOptionCount[1] = 0;
                voteController.VoteOptionCount[2] = 0;
                voteController.VoteOptionCount[3] = 0;
                voteController.VoteOptionCount[4] = 0;
            }
        }

        /// Clears all vote states on map change.
        public void OnMapStart(string mapName) => Cleanup();

        /// Store the default vote convars so Unload doesn't perm modify the server cvars
        public void OnLoad(Plugin plugin)
        {
            _voteConVarBaseline.Clear();
            foreach (var name in VoteConVarNames)
            {
                try
                {
                    var cvar = ConVar.Find(name);
                    if (cvar != null)
                        _voteConVarBaseline[name] = cvar.GetPrimitiveValue<bool>();
                }
                catch (Exception ex)
                {
                    _debugLogger.LogWarning(ex, "[RTV.PanoramaVote] Could not snapshot convar {ConVar}; it will not be restored on unload.", name);
                }
            }
        }

        public void Unload(Plugin plugin)
        {
            if (m_bIsVoteInProgress)
            {
                try
                {
                    EndVote(YesNoVoteEndReason.VoteEnd_Cancelled);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RTV.PanoramaVote] Failed to cancel the active vote during unload.");
                }
            }

            Cleanup();
            RestoreVoteConVars();
        }

        private static readonly string[] VoteConVarNames =
        [
            "sv_allow_votes",
            "sv_vote_allow_in_warmup",
            "sv_vote_allow_spectators",
            "sv_vote_count_spectator_votes"
        ];

        private readonly Dictionary<string, bool> _voteConVarBaseline = new();

        private void RestoreVoteConVars()
        {
            foreach (var (name, value) in _voteConVarBaseline)
                Server.ExecuteCommand($"{name} {(value ? 1 : 0)}");
        }

        public void Cleanup()
        {
            m_VoteTimer?.Kill();
            m_VoteTimer = null;
            m_bIsVoteInProgress = false;
            m_VoteHandler = null;
            m_VoteResult = null;
            m_szCurrentVoteTitle = string.Empty;
            m_szCurrentVoteDetailStr = string.Empty;
            CurrentVotefilter = new RecipientFilter();
            VoteController = null;
        }

        /// Initializes the vote controller if a vote is not already in progress.
        public void Init()
        {
            if (m_bIsVoteInProgress)
                return;

            var controllers = Utilities.FindAllEntitiesByDesignerName<CVoteController>("vote_controller");
            var pVoteController = controllers.LastOrDefault();
            if (pVoteController == null)
                return;

            VoteController = pVoteController;
        }

        /// Handles the event when a vote is cast by a player.
        public void VoteCast(EventVoteCast pEvent)
        {
            if (!m_bIsVoteInProgress || !HasValidController())
                return;

            if (m_VoteHandler != null)
            {
                var pVoter = pEvent.Userid;
                if (pVoter == null || !pVoter.IsValid)
                    return;
                m_VoteHandler(YesNoVoteAction.VoteAction_Vote, pVoter.Slot, pEvent.VoteOption);
            }

            UpdateVoteCounts();
            CheckForEarlyVoteClose();
        }

        private bool HasValidController() => VoteController != null && VoteController.IsValid;

        /// Removes a player from the current vote.
        public void RemovePlayerFromVote(int iSlot)
        {
            if (!m_bIsVoteInProgress) return;

            bool found = false;
            for (int i = 0; i < m_iVoterCount; i++)
            {
                if (m_iVoters[i] == iSlot)
                    found = true;
                else if (found)
                    m_iVoters[i - 1] = m_iVoters[i];
            }

            if (!found)
                return;

            m_iVoterCount--;
            m_iVoters[m_iVoterCount] = -1;

            if (!HasValidController())
                return;

            VoteController!.PotentialVotes = m_iVoterCount;
            UpdateVoteCounts();
            CheckForEarlyVoteClose();
        }

        /// Checks if a player is in the current vote pool.
        public bool IsPlayerInVotePool(int iSlot)
        {
            if (!m_bIsVoteInProgress)
                return false;

            if (iSlot < 0 || iSlot >= VoteConstants.MAXPLAYERS)
                return false;

            return m_iVoters.Contains(iSlot);
        }

        /// Updates the vote counts and fires a vote changed event.
        public void UpdateVoteCounts()
        {
            var vc = VoteController;
            if (vc == null || !vc.IsValid)
                return;

            var pEventPtr = NativeAPI.CreateEvent("vote_changed", true);

            NativeAPI.SetEventInt(pEventPtr, "vote_option1", vc.VoteOptionCount[0]);
            NativeAPI.SetEventInt(pEventPtr, "vote_option2", vc.VoteOptionCount[1]);
            NativeAPI.SetEventInt(pEventPtr, "vote_option3", vc.VoteOptionCount[2]);
            NativeAPI.SetEventInt(pEventPtr, "vote_option4", vc.VoteOptionCount[3]);
            NativeAPI.SetEventInt(pEventPtr, "vote_option5", vc.VoteOptionCount[4]);
            NativeAPI.SetEventInt(pEventPtr, "potentialVotes", m_iVoterCount);

            NativeAPI.FireEvent(pEventPtr, false);
        }

        /// Checks if a vote is currently in progress.
        public bool IsVoteInProgress()
        {
            return m_bIsVoteInProgress;
        }

        /// Start a new Yes/No vote for all players
        public bool SendYesNoVoteToAll(float flDuration, int iCaller, string sVoteTitle, string sDetailStr, YesNoVoteResult resultCallback, YesNoVoteHandler? handler = null)
        {
            CurrentVotefilter.Clear();
            foreach(var player in Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot && !p.IsHLTV && p.Connected == PlayerConnectedState.Connected))
            {
                CurrentVotefilter.Add(player);
            }

            return SendYesNoVote(flDuration, iCaller, sVoteTitle, sDetailStr, CurrentVotefilter, resultCallback, handler);
        }
        
        /// Starts a new Yes/No vote for Specific Players.
        public bool SendYesNoVote(float flDuration, int iCaller, string sVoteTitle, string sDetailStr, RecipientFilter pFilter, YesNoVoteResult resultCallback, YesNoVoteHandler? handler = null)
        {
            if (m_bIsVoteInProgress)
            {
                _debugLogger.LogWarning("[RTV.PanoramaVote] A vote is already in progress.");
                return false;
            }

            if (pFilter.Count <= 0)
                return false;

            if (resultCallback == null)
                return false;

            if (!HasValidController())
                Init();
            var vc = VoteController;
            if (vc == null || !vc.IsValid)
            {
                _logger.LogWarning("[RTV.PanoramaVote] No valid vote_controller entity, cannot start a panorama vote.");
                return false;
            }

            Reset(vc);

            m_bIsVoteInProgress = true;
            CurrentVotefilter = pFilter;
            InitVoters(pFilter);

            vc.PotentialVotes = m_iVoterCount;
            vc.ActiveIssueIndex = 2;

            m_VoteResult = resultCallback;
            m_VoteHandler = handler;

            m_iCurrentVoteCaller = iCaller;
            m_szCurrentVoteTitle = sVoteTitle;
            m_szCurrentVoteDetailStr = sDetailStr;

            UpdateVoteCounts();
            SendVoteStartUM(pFilter);

            m_VoteHandler?.Invoke(YesNoVoteAction.VoteAction_Start, 0, 0);

            int voteNum = m_iVoteCount;
            m_VoteTimer?.Kill();
            m_VoteTimer = new Timer(flDuration, () =>
            {
                m_VoteTimer = null;
                if (voteNum == m_iVoteCount)
                    EndVote(YesNoVoteEndReason.VoteEnd_TimeUp);
            }, TimerFlags.STOP_ON_MAPCHANGE);

            return true;
        }
        
        /// Sends a user message to start the vote.
        private void SendVoteStartUM(RecipientFilter pFilter)
        {
            UserMessage voteStart = UserMessage.FromId(346);
            voteStart.SetInt("team", -1);
            voteStart.SetInt("player_slot", m_iCurrentVoteCaller);
            voteStart.SetInt("vote_type", -1);  // -1 = csgo logo icon, 1 = Change a map icon
            voteStart.SetString("disp_str", m_szCurrentVoteTitle);
            voteStart.SetString("details_str", m_szCurrentVoteDetailStr);
            voteStart.SetBool("is_yes_no_vote", true);

            voteStart.Send(pFilter);
        }

        /// Initializes the voters for the current vote.
        private void InitVoters(RecipientFilter pFilter)
        {
            m_iVoterCount = 0;
            for (int i = 0; i < VoteConstants.MAXPLAYERS; i++)
            {
                m_iVoters[i] = -1;
            }

            m_iVoterCount = pFilter.Count;
            for (int i = 0, j = 0; i < m_iVoterCount; i++)
            {
                if (pFilter[i].Slot != -1)
                {
                    m_iVoters[j] = pFilter[i].Slot;
                    j++;
                }
            }
        }

        /// Checks if the vote can be closed early based on the number of votes cast.
        private void CheckForEarlyVoteClose()
        {
            var vc = VoteController;
            if (vc == null || !vc.IsValid)
                return;

            int votes = vc.VoteOptionCount[(int)CastVote.VOTE_OPTION1] + vc.VoteOptionCount[(int)CastVote.VOTE_OPTION2];
            if (votes >= m_iVoterCount)
            {
                Server.NextFrame(() => EndVote(YesNoVoteEndReason.VoteEnd_AllVotes));
            }
        }

        /// Cancels the current vote.
        public void CancelVote()
        {
            if (!m_bIsVoteInProgress)
                return;

            EndVote(YesNoVoteEndReason.VoteEnd_TimeUp);
        }
        
        /// Ends the current vote with the specified reason.
        public void EndVote(YesNoVoteEndReason reason, int overrideFailCode = -1)
        {
            if (!m_bIsVoteInProgress)
                return;

            m_bIsVoteInProgress = false;

            m_VoteTimer?.Kill();
            m_VoteTimer = null;

            if (m_iVoteCount == 99)
                m_iVoteCount = 0;
            else
                m_iVoteCount++;

            if (m_VoteHandler != null)
                m_VoteHandler(YesNoVoteAction.VoteAction_End, (int)reason, 0);

            var vc = VoteController;
            if (vc == null || !vc.IsValid)
            {
                SendVoteFailed(reason, overrideFailCode);
                return;
            }

            if (m_VoteResult == null || reason == YesNoVoteEndReason.VoteEnd_Cancelled)
            {
                SendVoteFailed(reason, overrideFailCode);
                vc.ActiveIssueIndex = -1;
                return;
            }

            var info = new YesNoVoteInfo
            {
                num_clients = m_iVoterCount,
                yes_votes   = vc.VoteOptionCount[(int)CastVote.VOTE_OPTION1],
                no_votes    = vc.VoteOptionCount[(int)CastVote.VOTE_OPTION2],
                num_votes   = vc.VoteOptionCount[(int)CastVote.VOTE_OPTION1]
                            + vc.VoteOptionCount[(int)CastVote.VOTE_OPTION2]
            };

            for (int i = 0; i < CurrentVotefilter.Count; i++)
            {
                if (i < m_iVoterCount)
                    info.clientInfo[i] = (m_iVoters[i], vc.VotesCast[m_iVoters[i]]);
                else
                    info.clientInfo[i] = (-1, -1);
            }

            bool passed = m_VoteResult(info);
            if (passed)
                SendVotePassed("#SFUI_vote_passed", "Vote Passed!");
            else
                SendVoteFailed(YesNoVoteEndReason.VoteEnd_TimeUp, 4); 
                // 0 = "Vote Failed." in pano ui descripiton. 4 = Not enough players voted. 5 = Server has disabled that issue.

        }

        private void SendVoteFailed(YesNoVoteEndReason reason, int failCodeOverride = -1)
        {
            UserMessage voteFailed = UserMessage.FromId(348);
            voteFailed.SetInt("team", -1);

            int codeToSend = failCodeOverride >= 0 ? failCodeOverride : (int)reason;
            voteFailed.SetInt("reason", codeToSend);

            var pFilter = new RecipientFilter();
            pFilter.AddAllPlayers();
            voteFailed.Send(pFilter);
        }

        /// Sends a user message indicating that the vote passed.
        private void SendVotePassed(string disp_str = "", string details_str = "")
        {
            UserMessage votePass = UserMessage.FromId(347);

            votePass.SetInt("team", -1);
            votePass.SetInt("vote_type", 2);
            votePass.SetString("disp_str", disp_str);
            votePass.SetString("details_str", details_str);

            RecipientFilter pFilter = new RecipientFilter();
            pFilter.AddAllPlayers();

            votePass.Send(pFilter);
        }
    }
}