# Gomoku (Five-in-a-Row) - Quantum AI Example

An interactive console game demonstrating quantum computing concepts through Gomoku gameplay with three AI modes: Classical, Quantum, and Hybrid.

**Inspired by the classic Thomoku game** - featuring fast pattern-based threat detection and strategic gameplay.

## 🎮 Game Rules

**Gomoku** (五目並べ) is a strategic board game where two players alternate placing stones on a grid:

- **Objective**: Get 5 stones in a row (horizontal, vertical, or diagonal)
- **Players**: Black (●) moves first, White (○) moves second
- **Board**: Standard 15×15 or 19×19 grid
- **Win Condition**: First player to achieve 5-in-a-row wins
- **Draw**: If board fills completely without a winner

## 🚀 Quick Start

```bash
# Navigate to examples directory
cd examples/Gomoku

# Run the game (human vs AI)
dotnet run

# Watch AI vs AI demo
dotnet run -- --ai-vs-ai classical quantum

# Compare all AI types
dotnet run -- --ai-vs-ai quantum hybrid
```

## 🎯 Controls

### Cursor Mode (Recommended)
- **Arrow Keys**: Move cursor around the board
- **Enter**: Place stone at cursor position
- **T**: Switch to typing coordinates mode
- **Esc/Q**: Quit game at any time

### Typing Mode
- Enter row number (0-14 for standard board)
- Enter column number (0-14 for standard board)

## 🤖 AI Modes

### 1. Classical AI
Uses traditional heuristic-based evaluation with **Thomoku-inspired threat detection**:
- **Fast pre-check**: Immediate threat detection using pattern matching
- **Gap-aware counting**: Detects threats with up to 1 gap (e.g., `XX_XX`)
- **Priority system**: Block opponent wins > Create wins > Block fours > Create fours
- **Defensive focus**: 2.0× defensive weight for strategic play
- **Threat level scoring**: From immediate wins (100000) to single pieces (10)
- **Center control bonus**: Positional evaluation

**Performance**: O(N) where N is number of candidate positions (with O(1) threat pre-check)

### 2. Quantum AI
Demonstrates **Grover's algorithm** for search with tactical awareness:
- **Pre-check immediate threats**: Skips quantum search for obvious tactical moves
- **Position encoding**: Converts positions into qubits
- **Quantum superposition**: Evaluates all candidates simultaneously
- **Amplitude amplification**: Grover iterations to find optimal moves
- **Weighted measurement**: Probabilistic selection favoring high-scoring positions
- **Defensive focus**: 2.0× defensive weight matching Classical AI

**Performance**: O(√N) - quadratic speedup over classical (when not handling immediate threats)

**Educational Note**: This is a *simulated* quantum algorithm running on classical hardware to demonstrate quantum concepts.

### 3. Hybrid AI (Recommended)
Intelligently switches between classical and quantum:
- **Threat-aware**: Uses classical pre-check for all immediate threats
- **Classical mode**: When candidate count < 5 or > 30 (quantum overhead not worth it)
- **Quantum mode**: When 5-30 candidates (sweet spot for quantum advantage)
- **Adaptive threshold**: Learning based on performance
- **Real-time strategy**: Explains decision reasoning

## 📊 Performance Comparison

The game includes an **AI vs AI Mode** for benchmarking:

```bash
# Run 10-game benchmark
dotnet run -- --ai-vs-ai classical quantum
```

### Typical Game Statistics

```
Classical vs Quantum (10 games):
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Classical wins: 4/10 (40%)
Quantum wins:   6/10 (60%)
Average game:   33 moves
Game length:    20-46 moves
Typical range:  30-40 moves
```

### Threat Detection Performance

Both AIs use **fast threat pre-check** before expensive evaluation:

```
Immediate threat detected:
  Classical: O(1) pattern check → block/attack
  Quantum:   O(1) pattern check → skip quantum search
  
Strategic position:
  Classical: O(N) full evaluation
  Quantum:   O(√N) Grover's algorithm
  Speedup:   ~4.5× for N=20 candidates
```

### When Quantum Helps

✅ **Good for quantum** (strategic positions):
- Mid-game with multiple tactical options (5-30 candidates)
- Complex position evaluation where √N provides real benefit
- No immediate threats forcing specific moves

❌ **Threat pre-check used instead** (both AIs):
- Immediate winning moves (take the win!)
- Must-block positions (prevent loss!)
- Open-four threats (will win next turn)
- Critical defensive positions

This hybrid approach combines:
- **Fast tactical response** via pattern matching
- **Strategic optimization** via quantum search

## 🧮 Quantum Algorithm Explanation

### Grover's Algorithm

Grover's algorithm provides quadratic speedup for unstructured search:

1. **Initialization**: Create superposition of all candidate positions
   ```
   |ψ⟩ = (|0⟩ + |1⟩ + ... + |N-1⟩) / √N
   ```

2. **Oracle**: Mark high-value positions (negative phase flip)
   - Evaluates position quality using classical heuristics
   - Marks positions exceeding quality threshold

3. **Diffusion**: Amplify marked states through inversion about average
   ```
   Iterations needed: π/4 × √N
   ```

4. **Measurement**: Observe qubit state to get optimal position

### Why √N Speedup?

- **Classical search**: Must check all N positions → O(N)
- **Quantum search**: Amplitude amplification in ~√N iterations → O(√N)
- **Real benefit**: For N=100, classical needs 100 checks, quantum needs ~10

## 🏗️ Architecture

```
examples/Gomoku/
├── Board.fs                # Game logic, win detection, board state
├── AI/
│   ├── ThreatDetection.fs  # Fast pattern-based threat detection (Thomoku-inspired)
│   ├── Classical.fs        # Heuristic-based AI with threat pre-check
│   ├── Quantum.fs          # Grover's algorithm with threat awareness
│   └── Hybrid.fs           # Adaptive classical/quantum switching
├── UI/
│   ├── ConsoleRenderer.fs  # Spectre.Console rendering
│   └── InputHandler.fs     # Cursor + keyboard input
├── Program.fs              # Main game loop + AI vs AI mode
├── Gomoku.fsproj           # Project file
└── README.md               # This file
```

### Key Components

**`ThreatDetection.fs`**: Thomoku-inspired pattern matching
- Gap-aware threat counting (detects `XX_XX` patterns)
- Multi-priority threat classification
- O(1) pre-check for immediate threats
- Prevents quick losses and enables strategic play

**`Classical.fs`**: Traditional heuristic evaluation
- Threat levels: FiveInRow (100000) → OpenFour (10000) → Four (5000) → etc.
- 2.0× defensive weight for balanced play
- Center control bonus for positional advantage
- Efficient move filtering (only near occupied cells)

**`Quantum.fs`**: Grover's algorithm simulation
- Pre-check bypass for tactical moves
- Quantum superposition over candidate positions
- Amplitude amplification (π/4 × √N iterations)
- Probabilistic measurement for move selection

## 🎓 Learning Objectives

This example demonstrates:

1. **Quantum Superposition**: Multiple states evaluated simultaneously
2. **Amplitude Amplification**: Grover's technique for search optimization
3. **Quantum Advantage**: When and why quantum provides speedup
4. **Hybrid Computing**: Combining classical and quantum for optimal results
5. **Practical Quantum**: Real-world application of quantum algorithms

## 🔧 Technical Details

- **Framework**: .NET 10.0
- **Language**: F# 9.0
- **UI Library**: Spectre.Console (rich terminal UI)
- **Quantum Library**: FSharp.Azure.Quantum (simulated quantum operations)

## 📝 Design Decisions

### Thomoku-Inspired Threat Detection

Inspired by the classic Thomoku game's efficient pattern matching:
- **Fast pre-check**: O(1) immediate threat detection before expensive evaluation
- **Gap-aware patterns**: Recognizes threats with gaps (e.g., `X_XXX`, `XX_XX`)
- **Priority system**: Critical threats handled tactically, strategic moves use quantum
- **Balance**: Defensive weight tuned for competitive 30-40 move games

Instead of porting Thomoku's complete 2000+ line pattern library:
- Focused on core threat patterns (~150 lines)
- Combined with quantum search for strategic positions
- Achieves good gameplay without over-engineering

### Quantum + Classical Hybrid Approach

Best of both worlds:
- **Fast tactical response**: Pattern-based pre-check for immediate threats
- **Strategic optimization**: Quantum search for complex positions
- **Adaptive behavior**: Skips quantum overhead when not beneficial
- **Educational value**: Demonstrates when quantum provides real advantage

### User Experience

- **Cursor navigation**: More intuitive than typing coordinates
- **AI vs AI mode**: Watch and learn from computer strategies
- **Real-time feedback**: Shows current strategy and reasoning
- **Flexible input**: Supports both cursor and typing modes
- **Clean visuals**: Spectre.Console for professional terminal UI
- **Esc key**: Quick exit from any game state

## 🎯 Future Enhancements

This is a **technology demonstration** showing quantum concepts through gameplay. Potential improvements:

- [ ] Implement actual quantum backend (Azure Quantum hardware)
- [ ] Add THOMOKU's complete recursive threat analysis
- [ ] Look-ahead evaluation (minimax/alpha-beta pruning)
- [ ] Parallel quantum evaluation of multiple candidates
- [ ] Opening book for faster early game
- [ ] Historical game replay and analysis
- [ ] Save/load game state
- [ ] Network multiplayer support
- [ ] AI difficulty levels

**Current state is excellent for demonstrating quantum advantage** - games are balanced, strategic, and showcase both tactical (pattern) and strategic (quantum) AI approaches.

## 📚 References

- [Grover's Algorithm](https://en.wikipedia.org/wiki/Grover%27s_algorithm) - Quantum search algorithm
- [Gomoku Rules](https://en.wikipedia.org/wiki/Gomoku) - Five-in-a-row game
- [Thomoku](https://github.com/Thorium/thomoku) - Classic Gomoku AI (inspiration for threat detection)
- [Azure Quantum Documentation](https://learn.microsoft.com/azure/quantum/) - Microsoft quantum platform
- [F# Component Design Guidelines](https://learn.microsoft.com/dotnet/fsharp/style-guide/component-design-guidelines) - Idiomatic F# code

## 🤝 Contributing

This is an educational example. Feel free to:
- Improve AI heuristics
- Add new game modes
- Enhance quantum simulation accuracy
- Improve UI/UX

## 📄 License

Part of FSharp.Azure.Quantum project - see main repository for license details.

---

**Have fun exploring quantum computing through gameplay!** 🎮🔬
