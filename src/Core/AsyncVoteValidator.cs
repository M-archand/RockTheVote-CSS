namespace cs2_rockthevote
{
    public class AsyncVoteValidator
    {
        private readonly float _votePercentage;

        public AsyncVoteValidator(float votePercentage)
        {
            _votePercentage = votePercentage;
        }

        public int RequiredVotes(int totalPlayers)
        {
            return (int)Math.Ceiling(totalPlayers * _votePercentage);
        }

        public bool CheckVotes(int numberOfVotes, int totalPlayers)
        {
            return numberOfVotes > 0 && numberOfVotes >= RequiredVotes(totalPlayers);
        }
    }
}