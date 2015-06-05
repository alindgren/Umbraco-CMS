﻿using System.Collections.Generic;
using System.Net;
using System.Web.Http;
using AutoMapper;
using Umbraco.Core;
using Umbraco.Core.Dictionary;
using Umbraco.Core.Models;
using Umbraco.Web.Models.ContentEditing;
using Umbraco.Web.Models.Mapping;
using Umbraco.Web.Mvc;
using Umbraco.Web.WebApi;
using System.Linq;
using Umbraco.Web.WebApi.Filters;
using Constants = Umbraco.Core.Constants;
using Newtonsoft.Json;
using Umbraco.Core.PropertyEditors;
using System;

namespace Umbraco.Web.Editors
{

    //TODO:  We'll need to be careful about the security on this controller, when we start implementing 
    // methods to modify content types we'll need to enforce security on the individual methods, we
    // cannot put security on the whole controller because things like GetAllowedChildren are required for content editing.

    /// <summary>
    /// An API controller used for dealing with content types
    /// </summary>
    [PluginController("UmbracoApi")]
    public class ContentTypeController : ContentTypeControllerBase
    {
        private ICultureDictionary _cultureDictionary;

        /// <summary>
        /// Constructor
        /// </summary>
        public ContentTypeController()
            : this(UmbracoContext.Current)
        {            
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="umbracoContext"></param>
        public ContentTypeController(UmbracoContext umbracoContext)
            : base(umbracoContext)
        {
        }


        /// <summary>
        /// Gets all user defined properties.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetAllPropertyTypeAliases()
        {
            return ApplicationContext.Services.ContentTypeService.GetAllPropertyTypeAliases();
        }


        public ContentTypeDisplay GetById(int id)
        {
            var ct = Services.ContentTypeService.GetContentType(id);
            if (ct == null)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }

            var dto = Mapper.Map<IContentType, ContentTypeDisplay>(ct);
            return dto;
        }

        public ContentTypeDisplay GetEmpty()
        {
            var ct = new ContentType(-1);
            var dto = Mapper.Map<IContentType, ContentTypeDisplay>(ct);
            return dto;
        }

        public ContentPropertyDisplay GetPropertyTypeScaffold(int id)
        {
            var dataTypeDiff = Services.DataTypeService.GetDataTypeDefinitionById(id);

            if (dataTypeDiff == null)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }

            var preVals = UmbracoContext.Current.Application.Services.DataTypeService.GetPreValuesCollectionByDataTypeId(id);
            var editor = PropertyEditorResolver.Current.GetByAlias(dataTypeDiff.PropertyEditorAlias);

            return new ContentPropertyDisplay()
            {
                Editor = dataTypeDiff.PropertyEditorAlias,
                Validation = new PropertyTypeValidation() { },
                View = editor.ValueEditor.View,
                Config = editor.PreValueEditor.ConvertDbToEditor(editor.DefaultPreValues, preVals)
            };
        }

        /// <summary>
        /// Returns all content type objects
        /// </summary>
        public IEnumerable<ContentTypeBasic> GetAll()
        {
            var types = Services.ContentTypeService.GetAllContentTypes();
            var basics = types.Select(Mapper.Map<IContentType, ContentTypeBasic>);

            return basics.Select(basic =>
            {
                basic.Name = TranslateItem(basic.Name);
                basic.Description = TranslateItem(basic.Description);
                return basic;
            });
        }

        /// <summary>
        /// Returns the allowed child content type objects for the content item id passed in
        /// </summary>
        /// <param name="contentId"></param>
        public IEnumerable<ContentTypeBasic> GetAllowedChildren(int contentId)
        {
            if (contentId == Constants.System.RecycleBinContent)
                return Enumerable.Empty<ContentTypeBasic>();

            IEnumerable<IContentType> types;
            if (contentId == Constants.System.Root)
            {
                types = Services.ContentTypeService.GetAllContentTypes().ToList();

                //if no allowed root types are set, just return everything
                if(types.Any(x => x.AllowedAsRoot))
                    types = types.Where(x => x.AllowedAsRoot);
            }
            else
            {
                var contentItem = Services.ContentService.GetById(contentId);
                if (contentItem == null)
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }

                var ids = contentItem.ContentType.AllowedContentTypes.Select(x => x.Id.Value).ToArray();
                
                if (ids.Any() == false) return Enumerable.Empty<ContentTypeBasic>();

                types = Services.ContentTypeService.GetAllContentTypes(ids).ToList();
            }

            var basics = types.Select(Mapper.Map<IContentType, ContentTypeBasic>).ToList();

            foreach (var basic in basics)
            {
                basic.Name = TranslateItem(basic.Name);
                basic.Description = TranslateItem(basic.Description);
            }

            return basics;
        }


        public ContentTypeDisplay PostSave(ContentTypeDisplay contentType)
        {
            
            var ctService = ApplicationContext.Services.ContentTypeService;

            //TODO: warn on content type alias conflicts
            //TODO: warn on property alias conflicts
            
            //TODO: Validate the submitted model

            var ctId = Convert.ToInt32(contentType.Id);

            if (ctId > 0)
            {
                //its an update to an existing
                IContentType found = ctService.GetContentType(ctId);
                if(found == null)
                    throw new HttpResponseException( HttpStatusCode.NotFound );

                Mapper.Map(contentType, found);
                ctService.Save(found);

                //map the saved item back to the content type (it should now get id etc set)
                Mapper.Map(found, contentType);
                return contentType;
            }
            else
            {
                //ensure alias is set
                if (string.IsNullOrEmpty(contentType.Alias))
                    contentType.Alias = contentType.Name.ToSafeAlias();

                contentType.Id = null;

                //save as new
                var newCt = Mapper.Map<IContentType>(contentType);
                
                ctService.Save(newCt);

                //map the saved item back to the content type (it should now get id etc set)
                Mapper.Map(newCt, contentType);
                return contentType;
            }

        }

        // TODO: This should really be centralized and used anywhere globalization applies.
        internal string TranslateItem(string text)
        {
            if (text == null)
            {
                return null;
            }

            if (text.StartsWith("#") == false)
                return text;

            text = text.Substring(1);
            return CultureDictionary[text].IfNullOrWhiteSpace(text);
        }

        private ICultureDictionary CultureDictionary
        {
            get
            {
                return
                    _cultureDictionary ??
                    (_cultureDictionary = CultureDictionaryFactoryResolver.Current.Factory.CreateDictionary());
            }
        }
    }
}