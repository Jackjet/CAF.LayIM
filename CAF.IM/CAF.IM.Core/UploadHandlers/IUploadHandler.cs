﻿using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;

namespace CAF.IM.Core.UploadHandlers
{
    [InheritedExport]
    public interface IUploadHandler
    {
        bool IsValid(string fileName, string contentType);
        Task<UploadResult> UploadFile(string fileName, string contentType, Stream stream);
    }
}