using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HaVacation.Server.Extensions
{
    public static  class Extensions
    {

        public static DateTimeOffset TrimMilliseconds(this DateTimeOffset dt)
        {
            return new DateTimeOffset(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, 0,dt.Offset );
        }
        public static DateTimeOffset TrimSeconds(this DateTimeOffset dt)
        {
            return new DateTimeOffset(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, 0, dt.Offset);
        }
        public static DateTimeOffset TrimMinutes(this DateTimeOffset dt)
        {
            return new DateTimeOffset(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, 0, dt.Offset);
        }

    

    }
}
