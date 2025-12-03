namespace FSharp.Azure.Quantum.MachineLearning

/// Adam (Adaptive Moment Estimation) optimizer for quantum machine learning.
///
/// Adam is an adaptive learning rate optimization algorithm that combines
/// momentum (first moment) and RMSprop (second moment) for faster convergence
/// and better performance compared to standard gradient descent.
///
/// Reference: Kingma & Ba, "Adam: A Method for Stochastic Optimization" (ICLR 2015)
module AdamOptimizer =

    /// Configuration for Adam optimizer
    type AdamConfig = {
        /// Learning rate (α) - step size for parameter updates
        LearningRate: float
        /// Beta1 (β₁) - exponential decay rate for first moment estimate (momentum)
        /// Typical value: 0.9
        Beta1: float
        /// Beta2 (β₂) - exponential decay rate for second moment estimate (RMSprop)
        /// Typical value: 0.999
        Beta2: float
        /// Epsilon (ε) - small constant for numerical stability
        /// Typical value: 1e-8
        Epsilon: float
    }

    /// Internal state maintained by Adam optimizer across iterations
    type AdamState = {
        /// First moment vector (exponentially weighted average of gradients)
        M: float array
        /// Second moment vector (exponentially weighted average of squared gradients)
        V: float array
        /// Time step counter (starts at 1, increments each update)
        T: int
    }

    /// Default Adam configuration following Kingma & Ba (2015) recommendations
    let defaultConfig = {
        LearningRate = 0.001
        Beta1 = 0.9
        Beta2 = 0.999
        Epsilon = 1e-8
    }

    /// Create initial Adam state for given parameter count
    ///
    /// Parameters:
    ///   numParams - Number of parameters to optimize
    ///
    /// Returns:
    ///   Initial AdamState with zero-initialized moment vectors
    let createState (numParams: int) : AdamState =
        {
            M = Array.zeroCreate numParams
            V = Array.zeroCreate numParams
            T = 0
        }

    /// Validate Adam configuration parameters
    let private validateConfig (config: AdamConfig) : Result<unit, string> =
        if config.LearningRate <= 0.0 then
            Error "Learning rate must be positive"
        elif config.Beta1 < 0.0 || config.Beta1 >= 1.0 then
            Error "Beta1 must be in range [0, 1)"
        elif config.Beta2 < 0.0 || config.Beta2 >= 1.0 then
            Error "Beta2 must be in range [0, 1)"
        elif config.Epsilon <= 0.0 then
            Error "Epsilon must be positive"
        else
            Ok ()

    /// Validate parameter and gradient dimensions match optimizer state
    let private validateDimensions (state: AdamState) (parameters: float array) (gradients: float array) : Result<unit, string> =
        if parameters.Length <> state.M.Length then
            Error (sprintf "Parameters length (%d) does not match optimizer state (%d)" parameters.Length state.M.Length)
        elif gradients.Length <> state.M.Length then
            Error (sprintf "Gradients length (%d) does not match optimizer state (%d)" gradients.Length state.M.Length)
        elif parameters.Length <> gradients.Length then
            Error (sprintf "Parameters length (%d) does not match gradients length (%d)" parameters.Length gradients.Length)
        else
            Ok ()

    /// Update parameters using Adam optimizer
    ///
    /// Algorithm (Kingma & Ba 2015):
    ///   t ← t + 1
    ///   m_t ← β₁ * m_{t-1} + (1 - β₁) * g_t        [momentum update]
    ///   v_t ← β₂ * v_{t-1} + (1 - β₂) * g_t²      [RMSprop update]
    ///   m̂_t ← m_t / (1 - β₁^t)                     [bias correction for first moment]
    ///   v̂_t ← v_t / (1 - β₂^t)                     [bias correction for second moment]
    ///   θ_t ← θ_{t-1} - α * m̂_t / (√v̂_t + ε)     [parameter update]
    ///
    /// Parameters:
    ///   config - Adam optimizer configuration
    ///   state - Current optimizer state (moment vectors, time step)
    ///   parameters - Current parameter values
    ///   gradients - Computed gradients for current parameters
    ///
    /// Returns:
    ///   Result containing (updated parameters, updated state) or error message
    let update
        (config: AdamConfig)
        (state: AdamState)
        (parameters: float array)
        (gradients: float array)
        : Result<float array * AdamState, string> =

        // Validate inputs
        match validateConfig config with
        | Error err -> Error err
        | Ok () ->
            match validateDimensions state parameters gradients with
            | Error err -> Error err
            | Ok () ->
                // Increment time step
                let t = state.T + 1
                let tFloat = float t

                // Precompute bias correction denominators
                let beta1PowerT = config.Beta1 ** tFloat
                let beta2PowerT = config.Beta2 ** tFloat
                let biasCorrection1 = 1.0 - beta1PowerT
                let biasCorrection2 = 1.0 - beta2PowerT

                // Update moment vectors and compute new parameters
                let newM = Array.zeroCreate parameters.Length
                let newV = Array.zeroCreate parameters.Length
                let newParams = Array.zeroCreate parameters.Length

                for i = 0 to parameters.Length - 1 do
                    let g = gradients.[i]

                    // Update biased first moment estimate (momentum)
                    newM.[i] <- config.Beta1 * state.M.[i] + (1.0 - config.Beta1) * g

                    // Update biased second moment estimate (RMSprop)
                    newV.[i] <- config.Beta2 * state.V.[i] + (1.0 - config.Beta2) * (g * g)

                    // Compute bias-corrected first moment estimate
                    let mHat = newM.[i] / biasCorrection1

                    // Compute bias-corrected second moment estimate
                    let vHat = newV.[i] / biasCorrection2

                    // Update parameter with adaptive learning rate
                    newParams.[i] <- parameters.[i] - config.LearningRate * mHat / (sqrt vHat + config.Epsilon)

                let newState = { M = newM; V = newV; T = t }
                Ok (newParams, newState)

    /// Convenience function to update parameters with default Adam configuration
    let updateWithDefaults
        (state: AdamState)
        (parameters: float array)
        (gradients: float array)
        : Result<float array * AdamState, string> =
        update defaultConfig state parameters gradients

    /// Get current learning rate adjusted by bias correction
    ///
    /// The effective learning rate changes over time due to bias correction:
    ///   α_effective = α * √(1 - β₂^t) / (1 - β₁^t)
    ///
    /// Parameters:
    ///   config - Adam optimizer configuration
    ///   state - Current optimizer state
    ///
    /// Returns:
    ///   Effective learning rate at current time step
    let getEffectiveLearningRate (config: AdamConfig) (state: AdamState) : float =
        if state.T = 0 then
            config.LearningRate
        else
            let tFloat = float state.T
            let beta1PowerT = config.Beta1 ** tFloat
            let beta2PowerT = config.Beta2 ** tFloat
            config.LearningRate * sqrt (1.0 - beta2PowerT) / (1.0 - beta1PowerT)

    /// Reset optimizer state (useful for restarting training)
    ///
    /// Parameters:
    ///   state - Current optimizer state
    ///
    /// Returns:
    ///   New state with zero-initialized moment vectors and time step reset to 0
    let resetState (state: AdamState) : AdamState =
        {
            M = Array.zeroCreate state.M.Length
            V = Array.zeroCreate state.V.Length
            T = 0
        }

    /// Create Adam configuration with custom parameters
    ///
    /// Parameters:
    ///   learningRate - Learning rate (α)
    ///   beta1 - First moment decay rate (β₁)
    ///   beta2 - Second moment decay rate (β₂)
    ///   epsilon - Numerical stability constant (ε)
    ///
    /// Returns:
    ///   Result containing AdamConfig or validation error
    let createConfig
        (learningRate: float)
        (beta1: float)
        (beta2: float)
        (epsilon: float)
        : Result<AdamConfig, string> =
        let config = {
            LearningRate = learningRate
            Beta1 = beta1
            Beta2 = beta2
            Epsilon = epsilon
        }
        match validateConfig config with
        | Ok () -> Ok config
        | Error err -> Error err
