using ExcelDataReader;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ContainerPacking.DemoApp.Models
{

    // singleton object of 
    public class ExcelDataContext
    {
        // creating an object of ExcelDataContext
        private static ExcelDataContext _instance = null;
        private static Object _mutex = new Object();
        // no instantiated available
        private ExcelDataContext(string path)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read);
            IExcelDataReader excelReader = ExcelReaderFactory.CreateOpenXmlReader(stream);

            DataSet result = excelReader.AsDataSet(new ExcelDataSetConfiguration()
            {
                ConfigureDataTable = (_) => new ExcelDataTableConfiguration()
                {
                    UseHeaderRow = true
                }
            });

            this.Sheets = result.Tables;
        }

        // accessing to ExcelDataContext singleton
        public static ExcelDataContext GetInstance(string path)
        {
            if (_instance == null)
            {
                lock (_mutex) // now I can claim some form of thread safety...
                {
                    if (_instance == null)
                    {
                        _instance = new ExcelDataContext(path);
                    }
                }
            }

            return _instance;
        }

        // the dataset of Excel
        public DataTableCollection Sheets { get; private set; }
    }
}
