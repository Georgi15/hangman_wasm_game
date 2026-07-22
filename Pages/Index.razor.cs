using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace Becenicha.Pages
{
    public partial class Index : IAsyncDisposable
    {
        private const string CookieNameKey = "hangman_player";
        private const string MatchesStorageKey = "hangman_matches";
        private DotNetObjectReference<Index> _dotNetRef;

        private string EnteredName;
        private string CurrentName;
        private string StatusMessage;
        private string NewOpponentName;
        private string NewSecretWord;
        private string AcceptSecretWord;
        private string CurrentGuess;
        private string GuessMessage;
        private string SelectedMatchId;
        private string OnlinePlayerId;
        private string OnlineMatchId;
        private string OnlineStatusMessage;
        private HubConnection _matchHub;
        private List<HangmanMatch> AllMatches = new();

        private IEnumerable<HangmanMatch> MyMatches => AllMatches
            .Where(m => m.Involves(CurrentName))
            .OrderBy(m => m.CreatedAt)
            .ToList();

        private IEnumerable<HangmanMatch> IncomingChallenges => MyMatches
            .Where(m => !m.IsReady && !m.IsCurrentPlayerCreator(CurrentName));

        private IEnumerable<HangmanMatch> ActiveMatches => MyMatches
            .Where(m => !m.BothCompleted)
            .OrderBy(m => m.CreatedAt)
            .ToList();

        private IEnumerable<HangmanMatch> FinishedMatches => MyMatches
            .Where(m => m.BothCompleted)
            .OrderByDescending(m => m.CreatedAt)
            .ToList();

        private HangmanMatch SelectedMatch => string.IsNullOrEmpty(SelectedMatchId)
            ? null
            : AllMatches.FirstOrDefault(m => m.MatchId == SelectedMatchId);

        private bool HasName => !string.IsNullOrWhiteSpace(CurrentName);

        protected override async Task OnInitializedAsync()
        {
            await LoadCookieNameAsync();
            await RegisterOnlinePlayerAsync();
            await StartRealtimeMatchHubAsync();
            await LoadMatchesAsync();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                _dotNetRef = DotNetObjectReference.Create(this);
                await JsRuntime.InvokeVoidAsync("hangmanApp.registerStorageListener", _dotNetRef);
            }
        }

        private async Task LoadCookieNameAsync()
        {
            var name = await JsRuntime.InvokeAsync<string>("hangmanApp.getCookie", CookieNameKey);
            if (!string.IsNullOrWhiteSpace(name))
            {
                CurrentName = name.Trim();
                EnteredName = CurrentName;
            }
        }

        private async Task SaveName()
        {
            var trimmed = EnteredName?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                StatusMessage = "Please enter a valid name.";
                return;
            }

            CurrentName = trimmed;
            await JsRuntime.InvokeVoidAsync("hangmanApp.setCookie", CookieNameKey, CurrentName, 365);
            StatusMessage = $"Your player name '{CurrentName}' is saved in a cookie.";
            await RegisterOnlinePlayerAsync();
            await LoadMatchesAsync();
        }

        private void ClearName()
        {
            CurrentName = null;
            EnteredName = null;
            SelectedMatchId = null;
            StatusMessage = "You can enter a different player name now.";
        }

        private sealed class OnlinePlayerPayload
        {
            public string PlayerId { get; set; }
            public string Name { get; set; }
        }

        private sealed class OnlineMatchmakingPayload
        {
            public string Status { get; set; }
            public string MatchId { get; set; }
            public string PlayerId { get; set; }
            public string OpponentId { get; set; }
            public MatchStatePayload Match { get; set; }
        }

        private sealed class MatchStatePayload
        {
            public string MatchId { get; set; }
            public string PlayerAId { get; set; }
            public string PlayerBId { get; set; }
            public string PlayerASecret { get; set; }
            public string PlayerBSecret { get; set; }
            public int PlayerAGuesses { get; set; }
            public int PlayerBGuesses { get; set; }
            public bool PlayerACompleted { get; set; }
            public bool PlayerBCompleted { get; set; }
            public bool Finished { get; set; }
        }

        private sealed class LiveMatchPayload
        {
            public string MatchId { get; set; }
            public string PlayerAId { get; set; }
            public string PlayerBId { get; set; }
            public string PlayerASecret { get; set; }
            public string PlayerBSecret { get; set; }
            public int PlayerAGuesses { get; set; }
            public int PlayerBGuesses { get; set; }
            public bool PlayerACompleted { get; set; }
            public bool PlayerBCompleted { get; set; }
            public bool Finished { get; set; }
        }

        private async Task RegisterOnlinePlayerAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentName) || !string.IsNullOrWhiteSpace(OnlinePlayerId))
            {
                return;
            }

            try
            {
                var response = await HttpClient.PostAsJsonAsync("http://localhost:5030/api/players/register", new { Name = CurrentName });
                if (response.IsSuccessStatusCode)
                {
                    var payload = await response.Content.ReadFromJsonAsync<OnlinePlayerPayload>();
                    OnlinePlayerId = payload?.PlayerId;
                    OnlineStatusMessage = $"Online player registered as {CurrentName}.";
                }
                else
                {
                    OnlineStatusMessage = "Could not register player with the online server.";
                }
            }
            catch
            {
                OnlineStatusMessage = "Online server is currently unavailable.";
            }
        }

        private async Task StartRealtimeMatchHubAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentName) || string.IsNullOrWhiteSpace(OnlinePlayerId))
            {
                return;
            }

            _matchHub = new HubConnectionBuilder()
                .WithUrl("http://localhost:5030/hangmanhub?playerId=" + Uri.EscapeDataString(OnlinePlayerId))
                .WithAutomaticReconnect()
                .Build();

            _matchHub.On<OnlineMatchmakingPayload>("MatchCreated", payload =>
            {
                OnlineMatchId = payload?.MatchId;
                OnlineStatusMessage = $"Live match created: {payload?.MatchId}";
                InvokeAsync(StateHasChanged);
            });

            _matchHub.On<LiveMatchPayload>("MatchUpdated", payload =>
            {
                OnlineStatusMessage = "Live match updated by your opponent.";
                if (payload != null && SelectedMatchId == payload.MatchId)
                {
                    _ = LoadMatchesAsync();
                }

                InvokeAsync(StateHasChanged);
            });

            await _matchHub.StartAsync();
        }

        private async Task JoinOnlineMatch()
        {
            if (string.IsNullOrWhiteSpace(OnlinePlayerId))
            {
                OnlineStatusMessage = "Register your player name first so the server can match you.";
                return;
            }

            try
            {
                var response = await HttpClient.PostAsJsonAsync("http://localhost:5030/api/matchmaking/join", new { PlayerId = OnlinePlayerId });
                if (!response.IsSuccessStatusCode)
                {
                    OnlineStatusMessage = "The server rejected the matchmaking request.";
                    return;
                }

                var payload = await response.Content.ReadFromJsonAsync<OnlineMatchmakingPayload>();
                if (payload?.Status == "matched")
                {
                    OnlineMatchId = payload.MatchId;
                    OnlineStatusMessage = $"Matched! Your live match id is {payload.MatchId}.";
                }
                else
                {
                    OnlineMatchId = null;
                    OnlineStatusMessage = "Waiting for another online player...";
                }
            }
            catch
            {
                OnlineStatusMessage = "Online server is currently unavailable.";
            }
        }

        private async Task RefreshOnlineStatus()
        {
            if (string.IsNullOrWhiteSpace(OnlinePlayerId))
            {
                OnlineStatusMessage = "Register your player name first.";
                return;
            }

            try
            {
                var response = await HttpClient.GetFromJsonAsync<OnlineMatchmakingPayload>($"http://localhost:5030/api/matchmaking/status/{OnlinePlayerId}");
                if (response?.Status == "matched" || response?.Status == "in_match")
                {
                    OnlineMatchId = response.MatchId;
                    OnlineStatusMessage = "You are already in a live match.";
                }
                else
                {
                    OnlineStatusMessage = "You are waiting in the queue.";
                }
            }
            catch
            {
                OnlineStatusMessage = "Online server is currently unavailable.";
            }
        }

        private async Task CreateChallenge()
        {
            GuessMessage = null;
            StatusMessage = null;

            var opponent = NewOpponentName?.Trim();
            var secret = NewSecretWord?.Trim();
            if (string.IsNullOrWhiteSpace(opponent) || string.IsNullOrWhiteSpace(secret))
            {
                StatusMessage = "Provide your opponent's name and the secret word.";
                return;
            }

            if (string.Equals(opponent, CurrentName, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "You cannot challenge yourself.";
                return;
            }

            var reverseMatch = AllMatches.FirstOrDefault(m =>
                string.Equals(m.PlayerA, opponent, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.PlayerB, CurrentName, StringComparison.OrdinalIgnoreCase));
            if (reverseMatch != null)
            {
                SelectedMatchId = reverseMatch.MatchId;
                StatusMessage = "A pending challenge already exists from that player. Open it to accept.";
                return;
            }

            var matchId = HangmanMatch.BuildMatchId(CurrentName, opponent);
            if (AllMatches.Any(m => string.Equals(m.MatchId, matchId, StringComparison.Ordinal)))
            {
                StatusMessage = "A challenge with that opponent already exists. Open it or choose a different name.";
                SelectedMatchId = matchId;
                return;
            }

            var match = new HangmanMatch
            {
                MatchId = matchId,
                PlayerA = CurrentName,
                PlayerB = opponent,
                PlayerASecret = secret,
                CreatedAt = DateTime.UtcNow
            };

            AllMatches.Add(match);
            await SaveMatchesAsync();
            SelectedMatchId = match.MatchId;
            NewOpponentName = string.Empty;
            NewSecretWord = string.Empty;
            StatusMessage = $"Challenge sent to {opponent}. They must accept and enter their secret word.";
        }

        private async Task AcceptChallenge()
        {
            GuessMessage = null;
            StatusMessage = null;
            if (SelectedMatch == null)
            {
                StatusMessage = "Select the challenge first.";
                return;
            }

            var secret = AcceptSecretWord?.Trim();
            if (string.IsNullOrWhiteSpace(secret))
            {
                StatusMessage = "Enter a secret word before accepting.";
                return;
            }

            if (SelectedMatch.IsCurrentPlayerCreator(CurrentName))
            {
                StatusMessage = "You already created this challenge.";
                return;
            }

            SelectedMatch.PlayerBSecret = secret;
            await SaveMatchesAsync();
            AcceptSecretWord = string.Empty;
            StatusMessage = "Challenge accepted. The match is ready to play.";
        }

        private async Task SubmitGuess()
        {
            if (SelectedMatch == null)
            {
                return;
            }

            GuessMessage = null;
            if (!SelectedMatch.IsReady)
            {
                GuessMessage = "Wait until both players have submitted their secret words.";
                return;
            }

            var guess = CurrentGuess?.Trim();
            if (string.IsNullOrWhiteSpace(guess))
            {
                GuessMessage = "Enter a letter or full word.";
                return;
            }

            var isCreator = SelectedMatch.IsCurrentPlayerCreator(CurrentName);
            var target = SelectedMatch.TargetWord(CurrentName);
            if (string.IsNullOrWhiteSpace(target))
            {
                GuessMessage = "The secret word is not available yet.";
                return;
            }

            var guessLetters = isCreator ? SelectedMatch.PlayerAGuessedLetters : SelectedMatch.PlayerBGuessedLetters;
            var guessHistory = isCreator ? SelectedMatch.PlayerAGuessHistory : SelectedMatch.PlayerBGuessHistory;

            if (guess.Length == 1)
            {
                var letter = char.ToLowerInvariant(guess[0]);
                if (!char.IsLetter(letter))
                {
                    GuessMessage = "Enter a valid letter or the full word.";
                    return;
                }

                if (guessLetters?.Contains(letter) == true)
                {
                    GuessMessage = $"You already guessed '{letter}'.";
                    CurrentGuess = string.Empty;
                    return;
                }

                guessLetters = (guessLetters ?? string.Empty) + letter;
                if (isCreator)
                    SelectedMatch.PlayerAGuessedLetters = guessLetters;
                else
                    SelectedMatch.PlayerBGuessedLetters = guessLetters;

                GuessMessage = $"Letter '{letter}' recorded.";
            }
            else
            {
                if (string.Equals(guess, target, StringComparison.OrdinalIgnoreCase))
                {
                    if (isCreator)
                        SelectedMatch.PlayerACompleted = true;
                    else
                        SelectedMatch.PlayerBCompleted = true;

                    if (isCreator)
                        SelectedMatch.PlayerAGuesses++;
                    else
                        SelectedMatch.PlayerBGuesses++;

                    GuessMessage = "Correct! You solved the word.";
                    CurrentGuess = string.Empty;
                    await SaveMatchesAsync();
                    return;
                }

                GuessMessage = $"'{guess}' is not the secret word.";
            }

            if (isCreator)
                SelectedMatch.PlayerAGuesses++;
            else
                SelectedMatch.PlayerBGuesses++;

            var historyEntry = guess.Length == 1 ? $"'{guess.ToLowerInvariant()} '" : $"\"{guess}\"";
            if (string.IsNullOrWhiteSpace(guessHistory))
                guessHistory = historyEntry;
            else
                guessHistory += ", " + historyEntry;

            if (isCreator)
                SelectedMatch.PlayerAGuessHistory = guessHistory;
            else
                SelectedMatch.PlayerBGuessHistory = guessHistory;

            if (SelectedMatch.MaskedWord(CurrentName).Replace(" ", string.Empty).Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                if (isCreator)
                    SelectedMatch.PlayerACompleted = true;
                else
                    SelectedMatch.PlayerBCompleted = true;

                GuessMessage = "You revealed the full word!";
            }

            CurrentGuess = string.Empty;
            await SaveMatchesAsync();
        }

        private async Task FinishMatch()
        {
            if (SelectedMatch == null)
                return;

            AllMatches.Remove(SelectedMatch);
            SelectedMatchId = null;
            await SaveMatchesAsync();
            StatusMessage = "Match finished. Return to the lobby to create or accept another challenge.";
        }

        private void SelectMatch(string matchId)
        {
            SelectedMatchId = matchId;
            StatusMessage = null;
            GuessMessage = null;
        }

        private async Task RefreshMatches()
        {
            await LoadMatchesAsync();
            StatusMessage = "Lobby refreshed.";
        }

        private async Task LoadMatchesAsync()
        {
            var raw = await JsRuntime.InvokeAsync<string>("hangmanApp.getLocalItem", MatchesStorageKey) ?? "[]";
            try
            {
                AllMatches = JsonSerializer.Deserialize<List<HangmanMatch>>(raw, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                }) ?? new List<HangmanMatch>();
            }
            catch
            {
                AllMatches = new List<HangmanMatch>();
            }

            if (!string.IsNullOrEmpty(SelectedMatchId) && !AllMatches.Any(m => m.MatchId == SelectedMatchId))
            {
                SelectedMatchId = null;
            }

            if (string.IsNullOrEmpty(SelectedMatchId))
            {
                var incoming = IncomingChallenges.FirstOrDefault();
                if (incoming != null)
                {
                    SelectedMatchId = incoming.MatchId;
                }
            }

            StateHasChanged();
        }

        private async Task SaveMatchesAsync()
        {
            var raw = JsonSerializer.Serialize(AllMatches);
            await JsRuntime.InvokeVoidAsync("hangmanApp.setLocalItem", MatchesStorageKey, raw);
        }

        [JSInvokable]
        public Task OnStorageChanged()
        {
            return LoadMatchesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _dotNetRef?.Dispose();
            if (_matchHub != null)
            {
                await _matchHub.DisposeAsync();
            }
        }

        private class HangmanMatch
        {
            public string MatchId { get; set; }
            public string PlayerA { get; set; }
            public string PlayerB { get; set; }
            public string PlayerASecret { get; set; }
            public string PlayerBSecret { get; set; }
            public int PlayerAGuesses { get; set; }
            public int PlayerBGuesses { get; set; }
            public bool PlayerACompleted { get; set; }
            public bool PlayerBCompleted { get; set; }
            public string PlayerAGuessHistory { get; set; }
            public string PlayerBGuessHistory { get; set; }
            public string PlayerAGuessedLetters { get; set; }
            public string PlayerBGuessedLetters { get; set; }
            public DateTime CreatedAt { get; set; }

            public static string BuildMatchId(string playerA, string playerB)
            {
                return $"{playerA.Trim().ToLowerInvariant()}|{playerB.Trim().ToLowerInvariant()}";
            }

            public bool Involves(string name)
            {
                return string.Equals(PlayerA, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(PlayerB, name, StringComparison.OrdinalIgnoreCase);
            }

            public bool IsCurrentPlayerCreator(string name)
            {
                return string.Equals(PlayerA, name, StringComparison.OrdinalIgnoreCase);
            }

            public string OpponentName(string name)
            {
                return string.Equals(PlayerA, name, StringComparison.OrdinalIgnoreCase) ? PlayerB : PlayerA;
            }

            public string OtherPlayerName(string name)
            {
                return OpponentName(name);
            }

            public bool MyCompleted(string name)
            {
                return IsCurrentPlayerCreator(name) ? PlayerACompleted : PlayerBCompleted;
            }

            public bool OpponentCompleted(string name)
            {
                return IsCurrentPlayerCreator(name) ? PlayerBCompleted : PlayerACompleted;
            }

            public bool BothCompleted => PlayerACompleted && PlayerBCompleted;

            public int MyGuesses(string name)
            {
                return IsCurrentPlayerCreator(name) ? PlayerAGuesses : PlayerBGuesses;
            }

            public int OpponentGuesses(string name)
            {
                return IsCurrentPlayerCreator(name) ? PlayerBGuesses : PlayerAGuesses;
            }

            public string MyGuessHistory(string name)
            {
                return IsCurrentPlayerCreator(name) ? PlayerAGuessHistory : PlayerBGuessHistory;
            }

            public string MyGuessedLetters(string name)
            {
                return IsCurrentPlayerCreator(name) ? PlayerAGuessedLetters : PlayerBGuessedLetters;
            }

            public string TargetWord(string name)
            {
                return IsCurrentPlayerCreator(name) ? PlayerBSecret : PlayerASecret;
            }

            public string MaskedWord(string name)
            {
                var target = TargetWord(name) ?? string.Empty;
                var letters = (MyGuessedLetters(name) ?? string.Empty).ToLowerInvariant();
                var result = string.Empty;

                foreach (var ch in target)
                {
                    if (!char.IsLetter(ch))
                    {
                        result += ch;
                    }
                    else if (letters.Contains(char.ToLowerInvariant(ch)))
                    {
                        result += ch;
                    }
                    else
                    {
                        result += '_';
                    }
                    result += ' ';
                }

                return result.Trim();
            }

            public string StateText(string name)
            {
                if (!IsReady)
                {
                    return IsCurrentPlayerCreator(name) ? "Waiting for opponent" : "Accept challenge";
                }

                if (!MyCompleted(name))
                {
                    return "Guessing";
                }

                if (!OpponentCompleted(name))
                {
                    return "Completed - waiting";
                }

                return "Finished";
            }

            public string ResultText(string name)
            {
                if (!BothCompleted)
                    return "Waiting for both players.";

                var mine = MyGuesses(name);
                var theirs = OpponentGuesses(name);
                if (mine < theirs)
                    return "You win!";
                if (mine > theirs)
                    return "You lose.";
                return "Draw.";
            }

            public bool IsReady => !string.IsNullOrWhiteSpace(PlayerASecret) && !string.IsNullOrWhiteSpace(PlayerBSecret);
        }
    }
}
