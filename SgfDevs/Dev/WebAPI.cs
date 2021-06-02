﻿using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using Umbraco.Core;
using Umbraco.Core.Composing;
using Umbraco.Web.WebApi;
using Examine;
using SgfDevs.ViewModels;
using Member = Umbraco.Web.PublishedModels.Member;
using Umbraco.Web;
using Umbraco.Core.Services;
using System;
using System.Net.Http;
using System.Net;
using Umbraco.Core;

namespace SgfDevs.Dev.WebAPI
{
    public class ProductsController : UmbracoApiController
    {
        [Route("api/tags/skills")]
        public IEnumerable<string> GetAllSkills()
        {
            var skills = DirectoryHelper.GetSkills().ToList();

            return skills.Select(x => string.IsNullOrEmpty(x.DisplayName) ? x.Name : x.DisplayName);
        }

        [Route("api/directory/filters/skills")]
        public IHttpActionResult GetSkillsFilters()
        {
            var _skills = DirectoryHelper.GetSkills().ToList();

            var skills = from x in _skills
                         select new
                         {
                             name = string.IsNullOrEmpty(x.DisplayName) ? x.Name : x.DisplayName,
                             id = x.Id,
                             key = x.Key,
                             isActive = false
                         };

            return Ok(skills.ToList());
        }

        [Route("api/directory/search")]
        public IHttpActionResult GetSearch()
        {
            var memberResults = new List<DirectoryResult>();
            IMemberService memberService = Services.MemberService;

            // This is pretty much one massive brainstorm clustrer eff at this point. Psh.
            if (ExamineManager.Instance.TryGetIndex(Constants.UmbracoIndexes.MembersIndexName, out var index))
            {
                var searcher = index.GetSearcher();
                var skillsQs = HttpContext.Current.Request.QueryString["skills"];

                // Send back all the members if no params are set
                if (skillsQs == null)
                {
                    var allMembers = DirectoryHelper.GetAllMembers();


                    foreach (var member in allMembers)
                    {
                        var node = Umbraco.Member(member.Id) as Member;
                        var url = "/member/" + member.Username;
                        var location = node.City + ", " + node.State;
                        var image = node.ProfileImage != null ? node.ProfileImage.GetCropUrl(width: 800) : "/images/pipey.jpg";
                        var isFoundingMember = node.HasValue("MemberTags") ? node.MemberTags.Where(n => n.Name.ToLower() == "founding member").Any() : false;

                        memberResults.Add(new DirectoryResult { Name = node.Name, Location = location, Image = image, Url = url, FoundingMember = isFoundingMember });
                    }

                    return Ok(memberResults);
                }

                // Otherwise hit Lucene/Examine
                var skillsParam = skillsQs.Split(',');
                var criteria = searcher.CreateQuery("member");
                var query = criteria.GroupedOr(new string[] { "nodeName", "skillKeys", "skillsTags", "skills", "skillIds" }, skillsParam);
                var results = query.Execute();

                if (results.Any())
                {
                    foreach (var result in results)
                    {
                        var member = memberService.GetById(int.Parse(result.Id));
                        var node = Umbraco.Member(result.Id) as Member;
                        var url = "/member/" + member.Username;
                        var location = node.City + ", " + node.State;
                        var image = node.ProfileImage != null ? node.ProfileImage.GetCropUrl(width: 800) : "/images/pipey.jpg";

                        memberResults.Add(new DirectoryResult { Name = node.Name, Location = location, Image = image, Url = url });
                    }

                    return Ok(memberResults.OrderBy(m => m.Name));
                }
            }

            return Ok(memberResults);
        }

        [Route("api/profile/image-process")]
        [HttpPost]
        [MemberAuthorize]
        public IHttpActionResult UploadProfileImage()
        {
            var member = (Member)Members.GetCurrentMember();

            var request = HttpContext.Current.Request;
            if (request.Files.Count > 0)
            {
                var file = request.Files[0];

                if(file != null && file.ContentLength > 0)
                {
                    var filesExtension = System.IO.Path.GetExtension(file.FileName);
                    var newFileName = member.Username + filesExtension;
                    var mediaService = Services.MediaService;
                    var membersMediaFolder = mediaService.GetRootMedia().FirstOrDefault(x => x.Name.InvariantEquals("Members"));

                    // Need to explore and see if mediaService.SetMediaFileContent will work to
                    // update an item if it already exists instead of always creating a new one.
                    // For now, just create dupes!
                    // - Myke
                    var media = mediaService.CreateMedia(member.Username, membersMediaFolder, Constants.Conventions.MediaTypes.Image);
                    media.SetValue(Services.ContentTypeBaseServices, Constants.Conventions.Media.File, newFileName, file.InputStream);
                    mediaService.Save(media);

                    var imageUdi = new GuidUdi("media", media.Key).ToString();

                    return Ok(imageUdi);
                }
            }
            
            return BadRequest();
        }
    }

    public class AttributeRoutingComponent : IComponent
    {
        public void Initialize()
        {
            GlobalConfiguration.Configuration.MapHttpAttributeRoutes();
            GlobalConfiguration.Configuration.Formatters.Remove(GlobalConfiguration.Configuration.Formatters.XmlFormatter);
        }

        public void Terminate()
        {

        }
    }

    public class AttributeRoutingComposer : IUserComposer
    {
        public void Compose(Composition composition)
        {
            composition.Components().Append<AttributeRoutingComponent>(); ;
        }
    }
}