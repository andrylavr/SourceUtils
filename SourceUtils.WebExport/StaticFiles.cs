﻿using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using MimeTypes;
using Ziks.WebServer;

namespace SourceUtils.WebExport
{
    partial class Program
    {
        private static readonly Dictionary<string, string> StaticFiles =
            new Dictionary<string, string>( StringComparer.InvariantCultureIgnoreCase )
            {
                {"/js/facepunch.webgame.js", Properties.Resources.facepunch_webgame},
                {"/js/sourceutils.js", Properties.Resources.sourceutils}
            };

        private static void CopyStaticFiles( string dir )
        {
            foreach ( var pair in StaticFiles )
            {
                var destPath = Path.Combine( dir, pair.Key.Substring( 1 ) ).Replace( '/', Path.DirectorySeparatorChar );
                var destDir = Path.GetDirectoryName( destPath );

                if ( destDir != null && !Directory.Exists( destDir ) ) Directory.CreateDirectory( destDir );

                File.WriteAllText( destPath, pair.Value );
            }
        }

        class StaticController : Controller
        {
            [Get( MatchAllUrl = false ), UsedImplicitly]
            public string GetFile()
            {
                var path = Request.Url.AbsolutePath;

                string value;
                if ( StaticFiles.TryGetValue( path, out value ) )
                {
                    Response.ContentLength64 = value.Length;
                    Response.ContentType = MimeTypeMap.GetMimeType( Path.GetExtension( path ) );
                    return value;
                }

                throw NotFoundException();
            }
        }

        private static void AddStaticFileControllers( Server server, string cacheDir = null )
        {
            if ( cacheDir == null )
            {
                server.Controllers.Add<StaticController>("/");
            }
            else
            {
                server.Controllers.Add( "/", () => new StaticFileController( cacheDir ) );
            }
        }
    }
}