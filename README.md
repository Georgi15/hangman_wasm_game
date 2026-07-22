# Hangman WASM Game

A Blazor WebAssembly multiplayer hangman duel game.

## Overview

- Save your player name in a browser cookie
- Create or accept challenges by player name
- Enter a secret word for your opponent
- Guess your opponent's word with letters or full-word guesses
- Finish matches and compare guess counts

## How to run

1. Open a terminal in the project folder (`d:\coding\Becenicha`).
2. Run `dotnet build`.
3. Run `dotnet run` or launch from your IDE.
4. Open `http://localhost:5259` in the browser.

## GitHub repo creation

If you have `gh` installed and authenticated with GitHub, run:

```bash
cd /d d:\coding\Becenicha

gh repo create "hangman-wasm-game" --public --source . --remote origin --push
```

If you only want to create the repo manually on GitHub, use this local push:

```bash
cd /d d:\coding\Becenicha

git remote add origin https://github.com/<your-username>/hangman-wasm-game.git

git push -u origin master
```

## Notes

- The local branch is currently `master`.
- Replace `<your-username>` with your GitHub username.
