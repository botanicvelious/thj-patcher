using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace THJPatcher
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public class FileListUrl : Attribute
    {
        public string Value { get; set; }
        public FileListUrl(string value)
        {
            Value = value;
        }
    }
}
