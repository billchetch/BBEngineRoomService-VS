using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Services;
using System.Configuration.Install;
using System.ComponentModel;

namespace BBAlarmsService
{

    [RunInstaller(true)]
    public class BBEngineRoomServiceInstaller : ServiceInstaller
    {
        public BBEngineRoomServiceInstaller() : base("BBEngineRoomService",
                                    "Bulan Baru Engine Room Service",
                                    "Runs an ADM service that gathers info from the engine room")
        {
            //empty
        }
    }
}