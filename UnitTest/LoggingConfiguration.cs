/*
 * Copyright 2019 Chris Morgan <chmorgan@gmail.com>
 *
 * GPLv3 licensed, see LICENSE for full text
 * Commercial licensing available
 */

using System.Reflection;
using log4net;
using log4net.Repository.Hierarchy;

namespace Test
{
    public class LoggingConfiguration
    {
        public static log4net.Core.Level GlobalLoggingLevel
        {
            get
            {
                Logger rootLogger = ((Hierarchy)LogManager.GetRepository(Assembly.GetCallingAssembly())).Root;
                return rootLogger.Level;
            }

            set
            {
                Logger rootLogger = ((Hierarchy)LogManager.GetRepository(Assembly.GetCallingAssembly())).Root;
                rootLogger.Level = value;
            }
        }
    }
}
