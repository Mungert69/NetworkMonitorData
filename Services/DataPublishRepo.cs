using System;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Objects.ServiceMessage;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
namespace NetworkMonitor.Objects.Repository
{
    public class DataPublishRepo
    {

        public static async Task FullProcessorList(ILogger logger, List<IRabbitRepo> rabbitRepos, List<ProcessorObj> processorObjs)
        {

            // publish to all systems.
            foreach (RabbitRepo rabbitRepo in rabbitRepos)
            {
               
                    var sendProcessorObjs = processorObjs.Where(w => w.RabbitHost == rabbitRepo.SystemUrl.RabbitHostName).ToList();
                    if (sendProcessorObjs != null && sendProcessorObjs.Count > 0)
                    {
                        await rabbitRepo.PublishAsync("fullProcessorList", sendProcessorObjs);
                        logger.LogInformation(" Published event fullProcessorList for RabbitHost = " + rabbitRepo.SystemUrl.RabbitHostName);
                    }
                
            }
        }
        public static async Task<bool> AddProcessor(ILogger logger, List<IRabbitRepo> rabbitRepos, ProcessorObj processorObj)
        {
            try
            {
                IRabbitRepo? rabbitRepo = rabbitRepos.Where(r => r.SystemUrl.RabbitHostName == processorObj.RabbitHost).FirstOrDefault();
                if (rabbitRepo != null)
                {
                    await rabbitRepo.PublishAsync<ProcessorObj>("addProcessor", processorObj);
                    logger.LogInformation(" Published event addProcessor for AppID = " + processorObj.AppID);
                    return true;
                }
                else
                {
                    logger.LogError($" Error : RabbitRepo for {processorObj.RabbitHost} can not be found");

                }
            }
            catch (Exception ex)
            {
                logger.LogError(" Error in PublishRepo.AddProcessor. Error was : " + ex.Message);
            }
            return false;
        }

        public static async Task<bool> UpdateProcessor(ILogger logger, List<IRabbitRepo> rabbitRepos, ProcessorObj processorObj)
        {
            try
            {
                IRabbitRepo? rabbitRepo = rabbitRepos.Where(r => r.SystemUrl.RabbitHostName == processorObj.RabbitHost).FirstOrDefault();
                if (rabbitRepo != null)
                {
                    await rabbitRepo.PublishAsync<ProcessorObj>("updateProcessor", processorObj);
                    logger.LogInformation(" Published event updateProcessor for AppID = " + processorObj.AppID);
                    return true;
                }
                else
                {
                    logger.LogError($" Error : RabbitRepo for {processorObj.RabbitHost} can not be found");

                }
            }
            catch (Exception ex)
            {
                logger.LogError(" Error in PublishRepo.UpdateProcessor. Error was : " + ex.Message);
            }
            return false;
        }

        public static async Task<bool> ProcessorAuthKey(ILogger logger, List<IRabbitRepo> rabbitRepos, ProcessorObj processorObj, ProcessorInitObj initObj)
        {
            try
            {
                IRabbitRepo? rabbitRepo = rabbitRepos.Where(r => r.SystemUrl.RabbitHostName == processorObj.RabbitHost).FirstOrDefault();
                if (rabbitRepo != null)
                {
                    await rabbitRepo.PublishAsync<ProcessorInitObj>($"processorAuthKey{processorObj.AppID}", initObj);
                    logger.LogInformation(" Published event ProcessorAuthKey for AppID = " + processorObj.AppID);
                    return true;
                }
                else
                {
                    logger.LogError($" Error : RabbitRepo for {processorObj.RabbitHost} can not be found");

                }
            }
            catch (Exception ex)
            {
                logger.LogError(" Error in PublishRepo.ProcessorAuthKey. Error was : " + ex.Message);
            }
            return false;
        }
        public static async Task<bool> ProcessorInit(ILogger logger, List<IRabbitRepo> rabbitRepos, ProcessorObj processorObj, ProcessorInitObj initObj)
        {
            try
            {
                IRabbitRepo? rabbitRepo = rabbitRepos.Where(r => r.SystemUrl.RabbitHostName == processorObj.RabbitHost).FirstOrDefault();
                if (rabbitRepo != null)
                {
                    await rabbitRepo.PublishAsync<ProcessorInitObj>("processorInit" + processorObj.AppID, initObj);
                    logger.LogInformation(" Published event ProcessorInit for AppID = " + processorObj.AppID);
                    return true;
                }
                else
                {
                    logger.LogError($" Error : RabbitRepo for {processorObj.RabbitHost} can not be found");

                }
            }
            catch (Exception ex)
            {
                logger.LogError(" Error in PublishRepo.ProcessorInit. Error was : " + ex.Message);
            }
            return false;
        }


    }
}