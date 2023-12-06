using System;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Data
{

    public class ProcessorState
    {
        private List<ProcessorObj> _processorList = new List<ProcessorObj>();
        private List<MonitorIP> _monitorIPs = new List<MonitorIP>();

        public List<ProcessorObj> FilteredProcessorList { get => _processorList.Where(w => w.Load < w.MaxLoad).ToList(); }
        public List<ProcessorObj> ProcessorList { get => _processorList; set => _processorList = value; }
        public List<MonitorIP> MonitorIPs { get => _monitorIPs; set => _monitorIPs = value; }

        public void SetAllLoads()
        {
            _processorList.ForEach(p =>
            {
                p.Load = _monitorIPs.Count(c => c.AppID == p.AppID);
            });
        }

        public void SetLoads(List<MonitorIP> beforeMonitorIPs, List<MonitorIP> afterMonitorIPs)
        {

            _processorList.ForEach(p =>
            {
                p.Load += afterMonitorIPs.Count(w => w.AppID == p.AppID) - beforeMonitorIPs.Count(w => w.AppID == p.AppID);
            });
        }

        public void RemoveLoad(string appID)
        {
            var processorObj = _processorList.Where(w => w.AppID == appID).FirstOrDefault();
            if (processorObj != null && processorObj.Load > 0)
            {
                processorObj.Load--;
            }
        }
        public void AddLoad(string appID)
        {
            var processorObj = _processorList.Where(w => w.AppID == appID).FirstOrDefault();
            if (processorObj != null)
            {
                processorObj.Load++;
            }
        }

        public string GetNextProcessorAppID(string endPointType)
        {
            var availableProcessors = _processorList.Where(o => !o.IsPrivate && o.Load < o.MaxLoad && (o.DisabledEndPointTypes == null || !o.DisabledEndPointTypes.Contains(endPointType))).ToList();

            if (availableProcessors.Count == 0)
            {
                return "0";
            }

            var processorObj = availableProcessors.OrderBy(o => o.Load).First();
            processorObj.Load++;
            return processorObj.AppID;
        }



    }
}