namespace FSharp.Azure.Quantum.MachineLearning.Tests

open Xunit
open FSharp.Azure.Quantum.MachineLearning
open System.IO

module ModelSerializationTests =
    
    // ========================================================================
    // TEST SETUP
    // ========================================================================
    
    /// Create a mock VQC training result for testing
    let private createMockTrainingResult () =
        {
            VQC.Parameters = [| 0.1; 0.2; 0.3; 0.4; 0.5 |]
            VQC.LossHistory = [1.5; 1.2; 0.9; 0.6; 0.3]
            VQC.Epochs = 5
            VQC.TrainAccuracy = 0.85
            VQC.Converged = true
        }
    
    /// Clean up test files
    let private cleanupTestFile (filePath: string) =
        if File.Exists filePath then
            File.Delete filePath
    
    // ========================================================================
    // BASIC SAVE/LOAD TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Save VQC model creates JSON file`` () =
        let testFile = "test_vqc_save.json"
        cleanupTestFile testFile
        
        try
            let parameters = [| 0.1; 0.2; 0.3 |]
            let result = 
                ModelSerialization.saveVQCModel
                    testFile
                    parameters
                    0.5
                    2
                    "ZZFeatureMap"
                    2
                    "RealAmplitudes"
                    1
                    (Some "Test model")
            
            match result with
            | Ok () ->
                Assert.True(File.Exists testFile, "JSON file should be created")
            | Error e ->
                Assert.Fail($"Save failed: {e}")
        finally
            cleanupTestFile testFile
    
    [<Fact>]
    let ``Load VQC model retrieves saved data`` () =
        let testFile = "test_vqc_load.json"
        cleanupTestFile testFile
        
        try
            let parameters = [| 0.5; 0.6; 0.7; 0.8 |]
            let finalLoss = 0.42
            
            // Save model
            match ModelSerialization.saveVQCModel testFile parameters finalLoss 2 "ZZFeatureMap" 2 "RealAmplitudes" 1 None with
            | Error e -> Assert.Fail($"Save failed: {e}")
            | Ok () ->
                
                // Load model
                match ModelSerialization.loadVQCModel testFile with
                | Error e -> Assert.Fail($"Load failed: {e}")
                | Ok model ->
                    Assert.Equal<float seq>(parameters, model.Parameters)
                    Assert.Equal(finalLoss, model.FinalLoss)
                    Assert.Equal(2, model.NumQubits)
                    Assert.Equal("ZZFeatureMap", model.FeatureMapType)
                    Assert.Equal(2, model.FeatureMapDepth)
                    Assert.Equal("RealAmplitudes", model.VariationalFormType)
                    Assert.Equal(1, model.VariationalFormDepth)
        finally
            cleanupTestFile testFile
    
    [<Fact>]
    let ``Load nonexistent file returns error`` () =
        let result = ModelSerialization.loadVQCModel "nonexistent_file_12345.json"
        
        match result with
        | Ok _ -> Assert.Fail("Should return error for nonexistent file")
        | Error msg ->
            Assert.Contains("not found", msg.ToLower())
    
    // ========================================================================
    // SAVE TRAINING RESULT TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Save VQC training result extracts final loss from history`` () =
        let testFile = "test_training_result.json"
        cleanupTestFile testFile
        
        try
            let result = createMockTrainingResult()
            
            match ModelSerialization.saveVQCTrainingResult testFile result 2 "ZZFeatureMap" 2 "RealAmplitudes" 1 None with
            | Error e -> Assert.Fail($"Save failed: {e}")
            | Ok () ->
                
                match ModelSerialization.loadVQCModel testFile with
                | Error e -> Assert.Fail($"Load failed: {e}")
                | Ok model ->
                    // Final loss should be last item in LossHistory
                    Assert.Equal(0.3, model.FinalLoss)
                    Assert.Equal<float seq>(result.Parameters, model.Parameters)
        finally
            cleanupTestFile testFile
    
    [<Fact>]
    let ``Save VQC training result handles empty loss history`` () =
        let testFile = "test_empty_loss.json"
        cleanupTestFile testFile
        
        try
            let result = { 
                createMockTrainingResult() with 
                    LossHistory = []
            }
            
            match ModelSerialization.saveVQCTrainingResult testFile result 2 "ZZFeatureMap" 2 "RealAmplitudes" 1 None with
            | Error e -> Assert.Fail($"Save failed: {e}")
            | Ok () ->
                
                match ModelSerialization.loadVQCModel testFile with
                | Error e -> Assert.Fail($"Load failed: {e}")
                | Ok model ->
                    Assert.Equal(0.0, model.FinalLoss)
        finally
            cleanupTestFile testFile
    
    // ========================================================================
    // PARAMETER LOADING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Load VQC parameters returns only parameter array`` () =
        let testFile = "test_params_only.json"
        cleanupTestFile testFile
        
        try
            let parameters = [| 1.0; 2.0; 3.0; 4.0; 5.0 |]
            
            match ModelSerialization.saveVQCModel testFile parameters 0.1 3 "ZZFeatureMap" 1 "RealAmplitudes" 1 None with
            | Error e -> Assert.Fail($"Save failed: {e}")
            | Ok () ->
                
                match ModelSerialization.loadVQCParameters testFile with
                | Error e -> Assert.Fail($"Load failed: {e}")
                | Ok loadedParams ->
                    Assert.Equal<float seq>(parameters, loadedParams)
        finally
            cleanupTestFile testFile
    
    // ========================================================================
    // MODEL INFO TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Get VQC model info returns metadata`` () =
        let testFile = "test_info.json"
        cleanupTestFile testFile
        
        try
            let parameters = [| 0.1; 0.2; 0.3; 0.4 |]
            let numQubits = 2
            let finalLoss = 0.25
            
            match ModelSerialization.saveVQCModel testFile parameters finalLoss numQubits "ZZFeatureMap" 2 "RealAmplitudes" 1 None with
            | Error e -> Assert.Fail($"Save failed: {e}")
            | Ok () ->
                
                match ModelSerialization.getVQCModelInfo testFile with
                | Error e -> Assert.Fail($"Get info failed: {e}")
                | Ok (qubits, numParams, loss, savedAt) ->
                    Assert.Equal(numQubits, qubits)
                    Assert.Equal(4, numParams)
                    Assert.Equal(finalLoss, loss)
                    Assert.NotEmpty(savedAt)
        finally
            cleanupTestFile testFile
    
    [<Fact>]
    let ``Print VQC model info displays metadata`` () =
        let testFile = "test_print_info.json"
        cleanupTestFile testFile
        
        try
            let parameters = [| 0.1; 0.2 |]
            
            match ModelSerialization.saveVQCModel testFile parameters 0.5 1 "ZFeatureMap" 1 "RY" 2 (Some "Test note") with
            | Error e -> Assert.Fail($"Save failed: {e}")
            | Ok () ->
                
                match ModelSerialization.printVQCModelInfo testFile with
                | Error e -> Assert.Fail($"Print info failed: {e}")
                | Ok () ->
                    Assert.True(true, "Print should succeed")
        finally
            cleanupTestFile testFile
    
    // ========================================================================
    // BATCH OPERATIONS TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Save VQC model batch creates multiple files`` () =
        let testBase = "test_batch"
        let testFiles = [| $"{testBase}_1.json"; $"{testBase}_2.json"; $"{testBase}_3.json" |]
        
        try
            Array.iter cleanupTestFile testFiles
            
            let models = [|
                ([| 0.1; 0.2 |], 0.5, Some "Model 1")
                ([| 0.3; 0.4 |], 0.4, Some "Model 2")
                ([| 0.5; 0.6 |], 0.3, None)
            |]
            
            match ModelSerialization.saveVQCModelBatch testBase models 2 "ZZFeatureMap" 2 "RealAmplitudes" 1 with
            | Error e -> Assert.Fail($"Batch save failed: {e}")
            | Ok fileNames ->
                Assert.Equal(3, fileNames.Length)
                Array.iter (fun file -> Assert.True(File.Exists file)) testFiles
        finally
            Array.iter cleanupTestFile testFiles
    
    [<Fact>]
    let ``Load VQC model batch loads all matching files`` () =
        let testDir = "test_batch_load"
        let testBase = Path.Combine(testDir, "model")
        
        try
            if Directory.Exists testDir then
                Directory.Delete(testDir, true)
            Directory.CreateDirectory(testDir) |> ignore
            
            let models = [|
                ([| 0.1 |], 0.9, None)
                ([| 0.2 |], 0.8, None)
            |]
            
            match ModelSerialization.saveVQCModelBatch testBase models 1 "ZZFeatureMap" 1 "RealAmplitudes" 1 with
            | Error e -> Assert.Fail($"Batch save failed: {e}")
            | Ok _ ->
                
                match ModelSerialization.loadVQCModelBatch testDir "model_*.json" with
                | Error e -> Assert.Fail($"Batch load failed: {e}")
                | Ok loadedModels ->
                    Assert.Equal(2, loadedModels.Length)
        finally
            if Directory.Exists testDir then
                Directory.Delete(testDir, true)
    
    [<Fact>]
    let ``Load VQC model batch from nonexistent directory returns error`` () =
        let result = ModelSerialization.loadVQCModelBatch "nonexistent_dir_12345" "*.json"
        
        match result with
        | Ok _ -> Assert.Fail("Should return error for nonexistent directory")
        | Error msg ->
            Assert.Contains("not found", msg.ToLower())
    
    [<Fact>]
    let ``Load VQC model batch with no matching files returns error`` () =
        let testDir = "test_empty_batch"
        
        try
            if Directory.Exists testDir then
                Directory.Delete(testDir, true)
            Directory.CreateDirectory(testDir) |> ignore
            
            let result = ModelSerialization.loadVQCModelBatch testDir "model_*.json"
            
            match result with
            | Ok _ -> Assert.Fail("Should return error when no files match")
            | Error msg ->
                Assert.Contains("no files", msg.ToLower())
        finally
            if Directory.Exists testDir then
                Directory.Delete(testDir, true)
    
    // ========================================================================
    // METADATA TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Saved model includes timestamp`` () =
        let testFile = "test_timestamp.json"
        cleanupTestFile testFile
        
        try
            let parameters = [| 0.1 |]
            
            match ModelSerialization.saveVQCModel testFile parameters 0.5 1 "ZFeatureMap" 1 "RY" 1 None with
            | Error e -> Assert.Fail($"Save failed: {e}")
            | Ok () ->
                
                match ModelSerialization.loadVQCModel testFile with
                | Error e -> Assert.Fail($"Load failed: {e}")
                | Ok model ->
                    Assert.NotEmpty(model.SavedAt)
                    // Verify timestamp format (ISO 8601)
                    Assert.Contains("T", model.SavedAt)
        finally
            cleanupTestFile testFile
    
    [<Fact>]
    let ``Saved model preserves optional note`` () =
        let testFile = "test_note.json"
        cleanupTestFile testFile
        
        try
            let parameters = [| 0.1; 0.2 |]
            let note = "This is a test model for XOR classification"
            
            match ModelSerialization.saveVQCModel testFile parameters 0.3 2 "ZZFeatureMap" 2 "RealAmplitudes" 1 (Some note) with
            | Error e -> Assert.Fail($"Save failed: {e}")
            | Ok () ->
                
                match ModelSerialization.loadVQCModel testFile with
                | Error e -> Assert.Fail($"Load failed: {e}")
                | Ok model ->
                    Assert.True(model.Note.IsSome)
                    Assert.Equal(note, model.Note.Value)
        finally
            cleanupTestFile testFile
    
    [<Fact>]
    let ``Saved model handles None note`` () =
        let testFile = "test_no_note.json"
        cleanupTestFile testFile
        
        try
            let parameters = [| 0.1 |]
            
            match ModelSerialization.saveVQCModel testFile parameters 0.5 1 "ZFeatureMap" 1 "RY" 1 None with
            | Error e -> Assert.Fail($"Save failed: {e}")
            | Ok () ->
                
                match ModelSerialization.loadVQCModel testFile with
                | Error e -> Assert.Fail($"Load failed: {e}")
                | Ok model ->
                    Assert.True(model.Note.IsNone)
        finally
            cleanupTestFile testFile
    
    // ========================================================================
    // ROUNDTRIP TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Complete save-load roundtrip preserves all data`` () =
        let testFile = "test_roundtrip.json"
        cleanupTestFile testFile
        
        try
            let originalParams = [| 0.123; 0.456; 0.789; 1.234; 5.678 |]
            let originalLoss = 0.0987
            let originalQubits = 3
            let originalFMType = "ZZFeatureMap"
            let originalFMDepth = 3
            let originalVFType = "EfficientSU2"
            let originalVFDepth = 2
            let originalNote = Some "Complete roundtrip test"
            
            // Save
            match ModelSerialization.saveVQCModel 
                    testFile 
                    originalParams 
                    originalLoss 
                    originalQubits 
                    originalFMType 
                    originalFMDepth 
                    originalVFType 
                    originalVFDepth 
                    originalNote with
            | Error e -> Assert.Fail($"Save failed: {e}")
            | Ok () ->
                
                // Load
                match ModelSerialization.loadVQCModel testFile with
                | Error e -> Assert.Fail($"Load failed: {e}")
                | Ok model ->
                    Assert.Equal<float seq>(originalParams, model.Parameters)
                    Assert.Equal(originalLoss, model.FinalLoss)
                    Assert.Equal(originalQubits, model.NumQubits)
                    Assert.Equal(originalFMType, model.FeatureMapType)
                    Assert.Equal(originalFMDepth, model.FeatureMapDepth)
                    Assert.Equal(originalVFType, model.VariationalFormType)
                    Assert.Equal(originalVFDepth, model.VariationalFormDepth)
                    Assert.Equal(originalNote, model.Note)
        finally
            cleanupTestFile testFile
    
    // ========================================================================
    // MULTI-CLASS VQC SERIALIZATION TESTS
    // ========================================================================
    
    /// Create a mock multi-class training result for testing
    let private createMockMultiClassResult () =
        {
            Classifiers = [|
                {
                    Parameters = [| 0.1; 0.2; 0.3 |]
                    LossHistory = [0.5; 0.3]
                    Epochs = 2
                    TrainAccuracy = 0.85
                    Converged = true
                }
                {
                    Parameters = [| 0.4; 0.5; 0.6 |]
                    LossHistory = [0.6; 0.4]
                    Epochs = 2
                    TrainAccuracy = 0.90
                    Converged = true
                }
                {
                    Parameters = [| 0.7; 0.8; 0.9 |]
                    LossHistory = [0.7; 0.5]
                    Epochs = 2
                    TrainAccuracy = 0.88
                    Converged = true
                }
            |]
            ClassLabels = [| 0; 1; 2 |]
            TrainAccuracy = 0.87
            NumClasses = 3
        } : VQC.MultiClassTrainingResult
    
    [<Fact>]
    let ``Save multi-class VQC model creates JSON file`` () =
        let testFile = "test_multiclass_save.json"
        cleanupTestFile testFile
        
        try
            let multiClassResult = createMockMultiClassResult()
            let result = 
                ModelSerialization.saveVQCMultiClassTrainingResult
                    testFile
                    multiClassResult
                    2
                    "ZZFeatureMap"
                    2
                    "RealAmplitudes"
                    1
                    (Some "Multi-class test model")
            
            match result with
            | Ok () ->
                Assert.True(File.Exists testFile, "JSON file should be created")
            | Error e ->
                Assert.Fail($"Save failed: {e}")
        finally
            cleanupTestFile testFile
    
    [<Fact>]
    let ``Load multi-class VQC model retrieves all classifiers`` () =
        let testFile = "test_multiclass_load.json"
        cleanupTestFile testFile
        
        try
            let multiClassResult = createMockMultiClassResult()
            
            // Save model
            match ModelSerialization.saveVQCMultiClassTrainingResult testFile multiClassResult 2 "ZZFeatureMap" 2 "RealAmplitudes" 1 None with
            | Error e -> Assert.Fail($"Save failed: {e}")
            | Ok () ->
                
                // Load model
                match ModelSerialization.loadVQCMultiClassModel testFile with
                | Error e -> Assert.Fail($"Load failed: {e}")
                | Ok model ->
                    // Check all classifiers loaded
                    Assert.Equal(3, model.Classifiers.Length)
                    Assert.Equal(3, model.NumClasses)
                    Assert.Equal<int seq>([| 0; 1; 2 |], model.ClassLabels)
                    Assert.Equal(0.87, model.TrainAccuracy, 2)
                    
                    // Check first classifier
                    Assert.Equal<float seq>([| 0.1; 0.2; 0.3 |], model.Classifiers.[0].Parameters)
                    Assert.Equal(0.85, model.Classifiers.[0].TrainAccuracy, 2)
                    Assert.Equal(2, model.Classifiers.[0].NumIterations)
                    
                    // Check architecture metadata
                    Assert.Equal(2, model.NumQubits)
                    Assert.Equal("ZZFeatureMap", model.FeatureMapType)
                    Assert.Equal(2, model.FeatureMapDepth)
                    Assert.Equal("RealAmplitudes", model.VariationalFormType)
                    Assert.Equal(1, model.VariationalFormDepth)
        finally
            cleanupTestFile testFile
    
    [<Fact>]
    let ``Multi-class roundtrip preserves all data`` () =
        let testFile = "test_multiclass_roundtrip.json"
        cleanupTestFile testFile
        
        try
            let originalResult = createMockMultiClassResult()
            let numQubits = 3
            let fmType = "ZZFeatureMap"
            let fmDepth = 2
            let vfType = "RealAmplitudes"
            let vfDepth = 1
            let note = Some "Roundtrip test for multi-class"
            
            // Save
            match ModelSerialization.saveVQCMultiClassTrainingResult 
                    testFile 
                    originalResult 
                    numQubits 
                    fmType 
                    fmDepth 
                    vfType 
                    vfDepth 
                    note with
            | Error e -> Assert.Fail($"Save failed: {e}")
            | Ok () ->
                
                // Load
                match ModelSerialization.loadVQCMultiClassModel testFile with
                | Error e -> Assert.Fail($"Load failed: {e}")
                | Ok model ->
                    // Verify all data preserved
                    Assert.Equal(originalResult.NumClasses, model.NumClasses)
                    Assert.Equal<int seq>(originalResult.ClassLabels, model.ClassLabels)
                    Assert.Equal(originalResult.TrainAccuracy, model.TrainAccuracy, 2)
                    Assert.Equal(originalResult.Classifiers.Length, model.Classifiers.Length)
                    
                    // Verify each classifier
                    for i in 0 .. originalResult.Classifiers.Length - 1 do
                        let original = originalResult.Classifiers.[i]
                        let loaded = model.Classifiers.[i]
                        Assert.Equal<float seq>(original.Parameters, loaded.Parameters)
                        Assert.Equal(original.TrainAccuracy, loaded.TrainAccuracy, 2)
                        Assert.Equal(original.Epochs, loaded.NumIterations)
                    
                    // Verify architecture
                    Assert.Equal(numQubits, model.NumQubits)
                    Assert.Equal(fmType, model.FeatureMapType)
                    Assert.Equal(fmDepth, model.FeatureMapDepth)
                    Assert.Equal(vfType, model.VariationalFormType)
                    Assert.Equal(vfDepth, model.VariationalFormDepth)
                    Assert.Equal(note, model.Note)
        finally
            cleanupTestFile testFile
    
    [<Fact>]
    let ``Load multi-class from nonexistent file returns error`` () =
        let result = ModelSerialization.loadVQCMultiClassModel "nonexistent_multiclass_12345.json"
        
        match result with
        | Ok _ -> Assert.Fail("Should return error for nonexistent file")
        | Error msg ->
            Assert.Contains("not found", msg.ToLower())
    
    [<Fact>]
    let ``Multi-class model preserves metadata timestamp`` () =
        let testFile = "test_multiclass_timestamp.json"
        cleanupTestFile testFile
        
        try
            let multiClassResult = createMockMultiClassResult()
            
            // Save
            match ModelSerialization.saveVQCMultiClassTrainingResult testFile multiClassResult 2 "ZZFeatureMap" 2 "RealAmplitudes" 1 None with
            | Error e -> Assert.Fail($"Save failed: {e}")
            | Ok () ->
                
                // Load and check timestamp exists
                match ModelSerialization.loadVQCMultiClassModel testFile with
                | Error e -> Assert.Fail($"Load failed: {e}")
                | Ok model ->
                    Assert.False(System.String.IsNullOrWhiteSpace(model.SavedAt), "SavedAt timestamp should be set")
        finally
            cleanupTestFile testFile
