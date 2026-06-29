using Matching;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class MatchingStateExtensionsTests
    {
        [Test]
        public void BrowsingRoomsはIsLoadingでない()
        {
            Assert.IsFalse(MatchingState.BrowsingRooms.IsLoading());
        }

        [Test]
        public void AuthenticatingはIsLoading()
        {
            Assert.IsTrue(MatchingState.Authenticating.IsLoading());
        }

        [Test]
        public void CreatingRoomはIsLoading()
        {
            Assert.IsTrue(MatchingState.CreatingRoom.IsLoading());
        }

        [Test]
        public void JoiningRoomはIsLoading()
        {
            Assert.IsTrue(MatchingState.JoiningRoom.IsLoading());
        }

        [Test]
        public void StartingはIsLoading()
        {
            Assert.IsTrue(MatchingState.Starting.IsLoading());
        }

        [Test]
        public void WaitingForPlayerはIsWaiting()
        {
            Assert.IsTrue(MatchingState.WaitingForPlayer.IsWaiting());
        }

        [Test]
        public void WaitingInCreatedRoomはIsWaiting()
        {
            Assert.IsTrue(MatchingState.WaitingInCreatedRoom.IsWaiting());
        }

        [Test]
        public void TimedOutはIsWaiting()
        {
            Assert.IsTrue(MatchingState.TimedOut.IsWaiting());
        }

        [Test]
        public void BrowsingRoomsはIsWaitingでない()
        {
            Assert.IsFalse(MatchingState.BrowsingRooms.IsWaiting());
        }
    }
}
