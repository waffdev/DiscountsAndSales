using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.IO;
using UnityEngine.Rendering;

namespace DiscountsAndSales.Embedded;

public static class Resources
{
    public static class Embedded { 
        public static byte[] texCardboard
        {
            get
            {
                AssemblyName asmName = Assembly.GetExecutingAssembly().GetName();
                string name = asmName.Name;
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{name}.Embedded.texCardboard.png"))
                {
                    return stream.ReadAllBytes();
                }
            }
        }

    }



}
