# Quantum Computing Glossary for F# Developers

Short definitions of the terms used across this documentation. Written for F# developers. No physics background assumed.

## The Basics

**Qubit** — The quantum version of a bit. A classical bit is `Zero` or `One`. A qubit holds a weighted mix of both at once. The weights are called amplitudes.

**Amplitude** — A complex number attached to each possible outcome. The squared size of an amplitude gives the probability of that outcome. Amplitudes can cancel each other out; probabilities cannot. That difference is where quantum speedups come from.

**Superposition** — The state of holding several outcomes at once, each with its own amplitude. Not the same as "unknown": the qubit really is in all of them until measured.

**Measurement** — Reading a qubit. It returns a plain 0 or 1, at random, with probabilities set by the amplitudes. Measurement destroys the superposition. You cannot peek without disturbing.

**Collapse** — What happens to the state during measurement: the superposition is replaced by the single outcome you observed.

**Entanglement** — A correlation between qubits that has no classical equivalent. Measuring one entangled qubit instantly fixes the probabilities of the others. Entangled states cannot be described qubit-by-qubit; only the whole group has a state.

**Gate** — The quantum version of an operation. A gate transforms qubit amplitudes. All gates are reversible. Common ones: H (make superposition), X (flip), CNOT (conditional flip, creates entanglement).

**Circuit** — A sequence of gates applied to qubits, usually ending in measurement. The quantum equivalent of a function pipeline.

**Unitary** — The mathematical property every gate must have. It means: reversible and probability-preserving. Think of it as a pure function that never loses information.

**Bloch sphere** — A picture of a single qubit as a point on a sphere. North pole = |0⟩, south pole = |1⟩, everything else = superpositions. Gates are rotations of the sphere.

**Ancilla** — An extra helper qubit. Algorithms add ancilla qubits as working memory: they hold intermediate values and are measured or reset at the end. Like a temporary variable in ordinary code.

**Oracle** — A circuit that answers a yes/no question about its input, used as a black box inside an algorithm. Grover's search, for example, needs an oracle that marks the item being searched for.

**Shots** — The number of times a circuit is run and measured. One run gives one random outcome. Many shots give a histogram of outcomes, e.g. `{"00": 498, "11": 502}`.

## Hardware and Execution

**Backend** — The thing that executes your circuit: a local simulator, a cloud simulator, or real quantum hardware. In this library, backends implement `IQuantumBackend`.

**NISQ** — "Noisy Intermediate-Scale Quantum": the current hardware era. Machines have 100–1000 imperfect qubits, no error correction, and short coherence times. Algorithms must be short and noise-tolerant.

**Decoherence** — The gradual loss of quantum behavior as qubits interact with their environment. It limits how long a computation can run. The main enemy of quantum hardware.

**Fidelity** — How close an operation comes to its ideal result, as a percentage. A gate fidelity of 99.9% means one error per thousand gates.

**Quantum error correction (QEC)** — Encoding one reliable "logical" qubit into many unreliable "physical" qubits, and constantly detecting and fixing errors. Needed for large algorithms; not available on today's machines at useful scale.

**Logical vs physical qubit** — A physical qubit is one real hardware device. A logical qubit is the error-corrected abstraction built out of ~1000 physical qubits. Algorithm papers count logical qubits; hardware specs count physical ones.

**QIR** — Quantum Intermediate Representation. A common compiler format for quantum programs, like IL for .NET. Circuits compile to QIR to run on different vendors' hardware.

## Algorithms

**Interference** — Amplitudes adding up or canceling out. Quantum algorithms are designed so wrong answers cancel and right answers reinforce. This is the actual mechanism of every quantum speedup.

**QFT (Quantum Fourier Transform)** — The quantum version of the discrete Fourier transform. Finds periodic patterns in amplitudes. The engine inside phase estimation and Shor's algorithm.

**QPE (Quantum Phase Estimation)** — Given an operation and one of its eigenstates, estimates the eigenvalue's phase to n bits. The core subroutine of quantum chemistry and Shor's algorithm. In this library: the `phaseEstimator` builder.

**Grover's algorithm** — Searches N unstructured items in about √N steps instead of N. A quadratic speedup, and provably the best possible. In this library it powers `quantumTreeSearch`, `constraintSolver`, and `patternMatcher`.

**Shor's algorithm** — Factors large integers in polynomial time by finding the period of a^x mod N. Breaks RSA once large error-corrected machines exist. In this library: the `periodFinder` builder.

**HHL** — A quantum algorithm for solving linear systems of equations. Exponentially faster than classical methods, but only under specific conditions on the matrix and the output.

**VQE (Variational Quantum Eigensolver)** — A hybrid loop: a quantum circuit with tunable parameters prepares a state, a classical optimizer adjusts the parameters to minimize energy. Used for chemistry on NISQ hardware.

**QAOA** — Quantum Approximate Optimization Algorithm. The optimization sibling of VQE: alternating problem and mixing layers, classically tuned. This library's optimization builders (graph coloring, MaxCut, TSP, portfolio) use QAOA underneath.

**Ansatz** — The shape of the parameterized circuit a variational algorithm tunes. German for "starting approach". Choosing an ansatz is like choosing a model architecture in machine learning.

**Hamiltonian** — The operator that encodes a system's energy. Optimization problems are solved by building a Hamiltonian whose lowest-energy state is the best solution.

**QUBO** — Quadratic Unconstrained Binary Optimization: a standard format for optimization problems (binary variables, quadratic cost). This library converts your business problem to QUBO before QAOA or annealing runs it.

**Quantum annealing** — A different way to compute: slowly evolve hardware toward the lowest-energy state of a problem Hamiltonian. D-Wave machines work this way. Good for optimization; not universal quantum computing.

## Topological Quantum Computing

See the [topological deep dive](topological/developer-deep-dive) for the full story.

**Quasiparticle** — Not a new fundamental particle. A collective pattern of many electrons that moves and behaves like a single particle — the way a bubble in water behaves like an object, although it is really a hole in the liquid. Anyons and Majorana zero modes are quasiparticles.

**Anyon** — A quasiparticle that exists only in 2D systems. Swapping two anyons changes the system's state in a way that depends only on the topology of the swap. The building block of topological quantum computing. Each anyon theory has a small fixed set of particle types: Ising has 1 (vacuum), σ (the anyon), and ψ (a fermion).

**Fusion** — Bringing two anyons together to see what single particle they combine into. For Ising anyons, a σ-pair fuses to either vacuum (read 0) or a fermion (read 1). Fusion is the topological version of measurement.

**Fusion channel** — The not-yet-decided outcome of a future fusion. A pair whose channel is still in superposition is a qubit. The channel belongs to the pair as a whole; no local noise can read it.

**Braiding** — Moving anyons around each other. Their paths through space and time form a braid, and each crossing applies a gate. The topological version of a circuit.

**Worldline** — The path a particle traces through space and time. Braids are made of crossing worldlines.

**Topological protection** — The fault tolerance of this whole approach: information is stored in global topology, which local noise cannot change. Errors require rare, drastic events instead of constant small disturbances.

**Majorana zero mode** — The real-world physical system expected to behave as an Ising anyon: a special electronic state at the ends of superconducting nanowires. The basis of Microsoft's hardware effort.

**Ising anyons** — The anyon theory closest to physical realization (via Majorana zero modes). Braiding them gives Clifford gates only, so universal computing additionally needs magic state distillation.

**Fibonacci anyons** — The theoretically ideal anyon: braiding alone is universal for quantum computing. No confirmed physical realization yet.

**Quantum dimension** — The growth rate of state space per anyon. Ising σ has dimension √2: one qubit per pair. A non-integer dimension signals that information is stored collectively, not in any single particle.

**Clifford gates** — The gate family (H, S, CNOT, ...) that is easy for error-corrected and topological hardware but not universal on its own — and efficiently simulatable classically. Adding a T gate makes the set universal.

**Magic state distillation** — A protocol that produces the missing T gate for Clifford-only hardware, by preparing many noisy "magic states" and distilling a few clean ones. Expensive but necessary for Ising-anyon universality.

**Toric code** — The classic topological error-correcting code: qubits on a grid, errors show up as particle pairs, correction means bringing the pairs back together. Implemented in this library's `ToricCode` module.
