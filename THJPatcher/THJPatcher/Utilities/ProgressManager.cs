using System;

namespace THJPatcher.Utilities
{
    /// <summary>
    /// Utility class for managing consistent progress tracking across the application
    /// </summary>
    public class ProgressManager
    {
        private readonly Action<int> _progressAction;
        private readonly int _totalScale;
        
        /// <summary>
        /// Initializes a new instance of the ProgressManager
        /// </summary>
        /// <param name="progressAction">Action to report progress</param>
        /// <param name="totalScale">Total scale of progress (default 10000 for 0-100.00%)</param>
        public ProgressManager(Action<int> progressAction, int totalScale = 10000)
        {
            _progressAction = progressAction ?? (progress => { });
            _totalScale = totalScale;
        }
        
        /// <summary>
        /// Reports progress for a specific operation within a defined range
        /// </summary>
        /// <param name="operationProgress">Current progress of the operation (0-1)</param>
        /// <param name="startProgress">Starting point in the total scale</param>
        /// <param name="progressWeight">Weight/range allocated to this operation</param>
        public void ReportProgress(double operationProgress, int startProgress, int progressWeight)
        {
            // Clamp operationProgress to 0-1
            operationProgress = Math.Max(0, Math.Min(1, operationProgress));
            
            // Calculate the adjusted progress within the allocated range
            int progress = startProgress + (int)(operationProgress * progressWeight);
            
            // Ensure we don't exceed the total scale
            progress = Math.Min(progress, _totalScale);
            
            // Report the progress
            _progressAction(progress);
        }
        
        /// <summary>
        /// Creates a progress callback for use with the UtilityLibrary download methods
        /// </summary>
        /// <param name="startProgress">Starting point in the total scale</param>
        /// <param name="progressWeight">Weight/range allocated to this operation</param>
        /// <returns>Progress callback action</returns>
        public Action<long, long> CreateDownloadCallback(int startProgress, int progressWeight)
        {
            return (bytesRead, totalBytes) => 
            {
                double operationProgress = totalBytes > 0 ? (double)bytesRead / totalBytes : 0;
                ReportProgress(operationProgress, startProgress, progressWeight);
            };
        }
        
        /// <summary>
        /// Creates a progress callback for operations processed in batches
        /// </summary>
        /// <param name="totalItems">Total number of items to process</param>
        /// <param name="startProgress">Starting point in the total scale</param>
        /// <param name="progressWeight">Weight/range allocated to this operation</param>
        /// <returns>Progress update action that takes the number of processed items</returns>
        public Action<int> CreateBatchCallback(int totalItems, int startProgress, int progressWeight)
        {
            return (processedItems) => 
            {
                double operationProgress = totalItems > 0 ? (double)processedItems / totalItems : 0;
                ReportProgress(operationProgress, startProgress, progressWeight);
            };
        }
    }
} 