using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Misuzilla.Applications.TwitterIrcGateway.AddIns.SqlServerDataStore;
using System.Data.Linq;

namespace TwitterIrcGatewayWeb.Models
{
    public interface ITimelineRepository
    {
        IQueryable<IGrouping<Group, Timeline>> FindByUserId(Int32 userId);
        void Delete(Timeline timeline);
        void Save();
    }

    public class TimelineRepository : ITimelineRepository
    {
        private TwitterIrcGatewayDataContext _dataContext = new TwitterIrcGatewayDataContext();
        
        public TimelineRepository()
        {
            DataLoadOptions dataLoadOptions = new DataLoadOptions();
            dataLoadOptions.LoadWith<Timeline>(t => t.Status);
            dataLoadOptions.LoadWith<Timeline>(t => t.User);
            _dataContext.LoadOptions = dataLoadOptions;
        }
        
        public IQueryable<IGrouping<Group, Timeline>> FindByUserId(Int32 userId)
        {
            var timelines = from timelineItem in _dataContext.Timeline
                            orderby timelineItem.StatusId descending 
                            where timelineItem.UserId == userId && timelineItem.Status != null
                            group timelineItem by timelineItem.Group;
            
            return timelines;
        }
    
        public void Save()
        {
            _dataContext.SubmitChanges();
        }

        public void Delete(Timeline timeline)
        {
            _dataContext.Timeline.DeleteOnSubmit(timeline);
        }
    }
}
