﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;

namespace HedgeModManager.Misc
{
    public static class Extensions
    {
        public static int GetDeterministicHashCode(this string str)
        {
            unchecked
            {
                int hash1 = (5381 << 16) + 5381;
                int hash2 = hash1;

                for (int i = 0; i < str.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1)
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }

        public static IEnumerable<TSource> DistinctBy<TSource, TKey>
            (this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            HashSet<TKey> seenKeys = new HashSet<TKey>();
            foreach (TSource element in source)
            {
                if (seenKeys.Add(keySelector(element)))
                {
                    yield return element;
                }
            }
        }

        public static async Task<T> GetAsJsonAsync<T>(this HttpClient client, string uri)
        {
            try
            {
                string result = await client.GetStringAsync(uri);
                return JsonConvert.DeserializeObject<T>(result);
            }
            catch
            {
                return default;
            }
        }

        public static async Task CopyToAsync(this HttpContent content, Stream destination, IProgress<double?> progress, CancellationToken cancellationToken = default)
        {
            progress?.Report(null);
            using (var source = await content.ReadAsStreamAsync().ConfigureAwait(false))
            {
                // adapted from CoreFX 
                // https://source.dot.net/#System.Private.CoreLib/Stream.cs,8048a9680abdd13b

                if (source is null)
                    throw new ArgumentNullException(nameof(source));
                if (destination is null)
                    throw new ArgumentNullException(nameof(destination));

                var contentLength = content.Headers.ContentLength;
                var bufferLength = 65536; // 64K seams reasonable?
                var buffer = ArrayPool<byte>.Shared.Rent(bufferLength); // prevents allocation of garbage arrays
                try
                {
                    int bytesRead;
                    int totalBytes = 0;
                    while ((bytesRead = await source.ReadAsync(buffer, 0, bufferLength, cancellationToken).ConfigureAwait(false)) != 0)
                    {
                        totalBytes += bytesRead;
                        await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);

                        if (contentLength.HasValue)
                        {
                            progress?.Report((double)totalBytes / contentLength.Value);
                        }
                        else
                        {
                            progress?.Report(null);
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        public static async Task DownloadFileAsync(this HttpClient client, string url, string filePath, IProgress<double?> progress = null)
        {
            using (var httpResponse = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                httpResponse.EnsureSuccessStatusCode();

                using (var outputFile = File.Create(filePath, 8192, FileOptions.Asynchronous))
                    await httpResponse.Content.CopyToAsync(outputFile, progress).ConfigureAwait(false);
            }
        }
    }

    public static class SyntaxFactoryEx
    {
        private static Dictionary<string, TypeSyntax> mTypeSyntaxes = new Dictionary<string, TypeSyntax>();
        private static Dictionary<string, SyntaxToken> mTokens = new Dictionary<string, SyntaxToken>();
        public static TypeSyntax GetTypeSyntax(string name)
        {
            if (mTypeSyntaxes.TryGetValue(name, out var value))
            {
                return value;
            }

            var type = SyntaxFactory.ParseTypeName(name);
            mTypeSyntaxes.Add(name, type);
            return type;
        }

        public static SyntaxToken GetToken(string name)
        {
            if (mTokens.TryGetValue(name, out var value))
            {
                return value;
            }

            var type = SyntaxFactory.ParseToken(name);
            mTokens.Add(name, type);
            return type;
        }

        public static MethodDeclarationSyntax MethodDeclaration(string identifier, string returnType, BlockSyntax body, params string[] modifiers)
        {
            var mods = new SyntaxToken[modifiers.Length];

            for (int i = 0; i < mods.Length; i++)
                mods[i] = GetToken(modifiers[i]);

            return SyntaxFactory.MethodDeclaration(new SyntaxList<AttributeListSyntax>(),
                SyntaxFactory.TokenList(mods), SyntaxFactory.PredefinedType(GetToken("void")), null, GetToken(identifier), null,
                SyntaxFactory.ParameterList(), new SyntaxList<TypeParameterConstraintClauseSyntax>(), body, null,
                SyntaxFactory.Token(SyntaxKind.None));
        }
    }
}
