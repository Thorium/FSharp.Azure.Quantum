namespace FSharp.Azure.Quantum.MachineLearning.Tests

open Xunit
open FSharp.Azure.Quantum.MachineLearning
open System.IO

module TransferLearningTests =
    
    // ========================================================================
    // TEST SETUP
    // ========================================================================
    
    let private cleanupTestFile (filePath: string) =
        if File.Exists filePath then
            File.Delete filePath
    
    let private createTestModel (filePath: string) =
        let parameters = [| 0.1; 0.2; 0.3; 0.4; 0.5; 0.6; 0.7; 0.8 |]  // 8 params = 4 layers * 2 params/layer
        ModelSerialization.saveVQCModel
            filePath
            parameters
            0.5
            2
            "ZZFeatureMap"
            2
            "RealAmplitudes"
            2
            (Some "Base model for transfer learning")
    
    // ========================================================================
    // LOAD FOR TRANSFER LEARNING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Load for transfer learning returns parameters and architecture`` () =
        let testFile = "test_transfer_load.json"
        cleanupTestFile testFile
        
        try
            match createTestModel testFile with
            | Error e -> Assert.Fail($"Setup failed: {e}")
            | Ok () ->
                
                match ModelSerialization.loadForTransferLearning testFile with
                | Error e -> Assert.Fail($"Load failed: {e}")
                | Ok (params, (numQubits, fmType, fmDepth, vfType, vfDepth)) ->
                    Assert.Equal(8, params.Length)
                    Assert.Equal(2, numQubits)
                    Assert.Equal("ZZFeatureMap", fmType)
                    Assert.Equal(2, fmDepth)
                    Assert.Equal("RealAmplitudes", vfType)
                    Assert.Equal(2, vfDepth)
        finally
            cleanupTestFile testFile
    
    // ========================================================================
    // INITIALIZE FOR FINE-TUNING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Initialize for fine-tuning with no frozen layers`` () =
        let pretrainedParams = [| 0.1; 0.2; 0.3; 0.4; 0.5; 0.6; 0.7; 0.8 |]
        let numLayers = 4
        let freezeLayers = 0
        
        match ModelSerialization.initializeForFineTuning pretrainedParams numLayers freezeLayers with
        | Error e -> Assert.Fail($"Init failed: {e}")
        | Ok (params, frozenIndices) ->
            Assert.Equal<float seq>(pretrainedParams, params)
            Assert.Empty(frozenIndices)
    
    [<Fact>]
    let ``Initialize for fine-tuning with frozen layers`` () =
        let pretrainedParams = [| 0.1; 0.2; 0.3; 0.4; 0.5; 0.6; 0.7; 0.8 |]
        let numLayers = 4
        let freezeLayers = 2  // Freeze first 2 layers (4 params)
        
        match ModelSerialization.initializeForFineTuning pretrainedParams numLayers freezeLayers with
        | Error e -> Assert.Fail($"Init failed: {e}")
        | Ok (params, frozenIndices) ->
            Assert.Equal<float seq>(pretrainedParams, params)
            Assert.Equal(4, frozenIndices.Length)  // 2 layers * 2 params/layer
            Assert.Equal([| 0; 1; 2; 3 |], frozenIndices)
    
    [<Fact>]
    let ``Initialize for fine-tuning rejects negative freeze layers`` () =
        let pretrainedParams = [| 0.1; 0.2; 0.3; 0.4 |]
        let numLayers = 2
        let freezeLayers = -1
        
        match ModelSerialization.initializeForFineTuning pretrainedParams numLayers freezeLayers with
        | Ok _ -> Assert.Fail("Should reject negative freezeLayers")
        | Error msg ->
            Assert.Contains("non-negative", msg)
    
    [<Fact>]
    let ``Initialize for fine-tuning rejects freeze layers exceeding total`` () =
        let pretrainedParams = [| 0.1; 0.2; 0.3; 0.4 |]
        let numLayers = 2
        let freezeLayers = 3
        
        match ModelSerialization.initializeForFineTuning pretrainedParams numLayers freezeLayers with
        | Ok _ -> Assert.Fail("Should reject freezeLayers > numLayers")
        | Error msg ->
            Assert.Contains("cannot exceed", msg)
    
    [<Fact>]
    let ``Initialize for fine-tuning rejects uneven parameter distribution`` () =
        let pretrainedParams = [| 0.1; 0.2; 0.3 |]  // 3 params, can't divide evenly
        let numLayers = 2
        let freezeLayers = 1
        
        match ModelSerialization.initializeForFineTuning pretrainedParams numLayers freezeLayers with
        | Ok _ -> Assert.Fail("Should reject uneven parameter distribution")
        | Error msg ->
            Assert.Contains("not evenly divisible", msg)
    
    // ========================================================================
    // UPDATE PARAMETERS WITH FROZEN LAYERS TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Update parameters respects frozen layers`` () =
        let currentParams = [| 1.0; 2.0; 3.0; 4.0; 5.0; 6.0 |]
        let gradients = [| 0.1; 0.2; 0.3; 0.4; 0.5; 0.6 |]
        let learningRate = 0.1
        let frozenIndices = [| 0; 1; 2 |]  // Freeze first 3 params
        
        let updated = ModelSerialization.updateParametersWithFrozenLayers currentParams gradients learningRate frozenIndices
        
        // First 3 params should be unchanged
        Assert.Equal(1.0, updated.[0])
        Assert.Equal(2.0, updated.[1])
        Assert.Equal(3.0, updated.[2])
        
        // Last 3 params should be updated
        Assert.Equal(3.96, updated.[3], 5)  // 4.0 - 0.1 * 0.4
        Assert.Equal(5.95, updated.[4], 5)  // 5.0 - 0.1 * 0.5
        Assert.Equal(5.94, updated.[5], 5)  // 6.0 - 0.1 * 0.6
    
    [<Fact>]
    let ``Update parameters with no frozen layers updates all`` () =
        let currentParams = [| 1.0; 2.0; 3.0 |]
        let gradients = [| 0.5; 0.5; 0.5 |]
        let learningRate = 0.1
        let frozenIndices = [| |]  // No frozen layers
        
        let updated = ModelSerialization.updateParametersWithFrozenLayers currentParams gradients learningRate frozenIndices
        
        // All params should be updated
        Assert.Equal(0.95, updated.[0], 5)
        Assert.Equal(1.95, updated.[1], 5)
        Assert.Equal(2.95, updated.[2], 5)
    
    // ========================================================================
    // MODEL COMPATIBILITY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Compatible models return true`` () =
        let testFile1 = "test_compat_1.json"
        let testFile2 = "test_compat_2.json"
        cleanupTestFile testFile1
        cleanupTestFile testFile2
        
        try
            // Create two models with same architecture
            let params1 = [| 0.1; 0.2; 0.3; 0.4 |]
            let params2 = [| 0.5; 0.6; 0.7; 0.8 |]
            
            match ModelSerialization.saveVQCModel testFile1 params1 0.5 2 "ZZFeatureMap" 2 "RealAmplitudes" 1 None with
            | Error e -> Assert.Fail($"Save 1 failed: {e}")
            | Ok () ->
                
                match ModelSerialization.saveVQCModel testFile2 params2 0.3 2 "ZZFeatureMap" 2 "RealAmplitudes" 1 None with
                | Error e -> Assert.Fail($"Save 2 failed: {e}")
                | Ok () ->
                    
                    match ModelSerialization.areModelsCompatible testFile1 testFile2 with
                    | Error e -> Assert.Fail($"Compatibility check failed: {e}")
                    | Ok compatible ->
                        Assert.True(compatible)
        finally
            cleanupTestFile testFile1
            cleanupTestFile testFile2
    
    [<Fact>]
    let ``Incompatible models return false - different qubits`` () =
        let testFile1 = "test_incompat_1.json"
        let testFile2 = "test_incompat_2.json"
        cleanupTestFile testFile1
        cleanupTestFile testFile2
        
        try
            let params1 = [| 0.1; 0.2 |]
            let params2 = [| 0.5; 0.6 |]
            
            match ModelSerialization.saveVQCModel testFile1 params1 0.5 2 "ZZFeatureMap" 2 "RealAmplitudes" 1 None with
            | Error e -> Assert.Fail($"Save 1 failed: {e}")
            | Ok () ->
                
                match ModelSerialization.saveVQCModel testFile2 params2 0.3 3 "ZZFeatureMap" 2 "RealAmplitudes" 1 None with
                | Error e -> Assert.Fail($"Save 2 failed: {e}")
                | Ok () ->
                    
                    match ModelSerialization.areModelsCompatible testFile1 testFile2 with
                    | Error e -> Assert.Fail($"Compatibility check failed: {e}")
                    | Ok compatible ->
                        Assert.False(compatible)
        finally
            cleanupTestFile testFile1
            cleanupTestFile testFile2
    
    [<Fact>]
    let ``Incompatible models return false - different variational form`` () =
        let testFile1 = "test_incompat_vf_1.json"
        let testFile2 = "test_incompat_vf_2.json"
        cleanupTestFile testFile1
        cleanupTestFile testFile2
        
        try
            let params1 = [| 0.1; 0.2 |]
            let params2 = [| 0.5; 0.6 |]
            
            match ModelSerialization.saveVQCModel testFile1 params1 0.5 2 "ZZFeatureMap" 2 "RealAmplitudes" 1 None with
            | Error e -> Assert.Fail($"Save 1 failed: {e}")
            | Ok () ->
                
                match ModelSerialization.saveVQCModel testFile2 params2 0.3 2 "ZZFeatureMap" 2 "EfficientSU2" 1 None with
                | Error e -> Assert.Fail($"Save 2 failed: {e}")
                | Ok () ->
                    
                    match ModelSerialization.areModelsCompatible testFile1 testFile2 with
                    | Error e -> Assert.Fail($"Compatibility check failed: {e}")
                    | Ok compatible ->
                        Assert.False(compatible)
        finally
            cleanupTestFile testFile1
            cleanupTestFile testFile2
    
    // ========================================================================
    // FEATURE EXTRACTOR TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Extract feature extractor returns subset of parameters`` () =
        let testFile = "test_feature_extractor.json"
        cleanupTestFile testFile
        
        try
            match createTestModel testFile with
            | Error e -> Assert.Fail($"Setup failed: {e}")
            | Ok () ->
                
                let numLayers = 4
                let extractLayers = 2
                
                match ModelSerialization.extractFeatureExtractor testFile numLayers extractLayers with
                | Error e -> Assert.Fail($"Extract failed: {e}")
                | Ok extractedParams ->
                    Assert.Equal(4, extractedParams.Length)  // 2 layers * 2 params/layer
                    Assert.Equal([| 0.1; 0.2; 0.3; 0.4 |], extractedParams)
        finally
            cleanupTestFile testFile
    
    [<Fact>]
    let ``Extract feature extractor rejects extractLayers exceeding total`` () =
        let testFile = "test_feature_extractor_invalid.json"
        cleanupTestFile testFile
        
        try
            match createTestModel testFile with
            | Error e -> Assert.Fail($"Setup failed: {e}")
            | Ok () ->
                
                let numLayers = 4
                let extractLayers = 5
                
                match ModelSerialization.extractFeatureExtractor testFile numLayers extractLayers with
                | Ok _ -> Assert.Fail("Should reject extractLayers > numLayers")
                | Error msg ->
                    Assert.Contains("cannot exceed", msg)
        finally
            cleanupTestFile testFile
    
    [<Fact>]
    let ``Extract all layers returns full parameter set`` () =
        let testFile = "test_extract_all.json"
        cleanupTestFile testFile
        
        try
            match createTestModel testFile with
            | Error e -> Assert.Fail($"Setup failed: {e}")
            | Ok () ->
                
                let numLayers = 4
                let extractLayers = 4  // Extract all layers
                
                match ModelSerialization.extractFeatureExtractor testFile numLayers extractLayers with
                | Error e -> Assert.Fail($"Extract failed: {e}")
                | Ok extractedParams ->
                    Assert.Equal(8, extractedParams.Length)
                    Assert.Equal([| 0.1; 0.2; 0.3; 0.4; 0.5; 0.6; 0.7; 0.8 |], extractedParams)
        finally
            cleanupTestFile testFile
