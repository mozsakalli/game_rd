// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;

namespace UnityEngine.Networking
{
    ///<summary>An interface for composition of data into multipart forms.</summary>
    ///<remarks>In order to provide a finer level of control for those wishing to generate multipart form data, but without forcing most users to refer to &lt;a href="http://tools.ietf.org/html/rfc2388"&gt;RFC 2388&lt;/a&gt;, Unity provides this simple interface which the UnityWebRequest API can use to serialize complex data into properly-formatted bytes.
    ///
    ///For convenience, the two general types of form sections have been encapsulated into two stock implementations of IMultipartFormSection. Both stock implementations are simply controlled via their constructors.
    ///
    ///IMultipartFormSection implementors are converted into bytes via <see cref="UnityWebRequest.SerializeFormSections" />.</remarks>
    ///<seealso cref="MultipartFormDataSection" />
    ///<seealso cref="MultipartFormFileSection" />
    public interface IMultipartFormSection
    {
        ///<summary>Returns the name of this section, if any.</summary>
        ///<remarks>Returns the name of this section; this is equivalent to the name of the form field which this section represents. In HTML terms, it is the name attribute on the input element represented by this form section.
        ///
        ///If this property returns null, the section is assumed to be unnamed.</remarks>
        ///<returns>The section's name, or <c>null</c>.</returns>
        string sectionName { get; }
        ///<summary>Returns the raw binary data contained in this section. Must not return null or a zero-length array.</summary>
        ///<returns>The raw binary data contained in this section. Must not be null or empty.</returns>
        byte[] sectionData { get; }
        ///<summary>Returns a string denoting the desired filename of this section on the destination server.</summary>
        ///<remarks>If this property returns a non-null string, then this is assumed to be a file section, and the file's name will be defined by the returned string.
        ///
        ///If you do not wish your section to be a file section, simply return <c>null</c> from this property.</remarks>
        ///<returns>The desired file name of this section, or <c>null</c> if this is not a file section.</returns>
        string fileName { get; } // return null if not a file section
        ///<summary>Returns the value to use in the <c>Content-Type</c> header for this form section.</summary>
        ///<remarks>If this property returns a non-null, non-empty string, then the returned string will be set as the <c>Content-Type</c> of this form section.
        ///
        ///If this property returns null or an empty string, then the <c>Content-Type</c> header will be omitted from this form section. How the server will handle this data is left up to the individual server.</remarks>
        ///<returns>The value to use in the <c>Content-Type</c> header, or <c>null</c>.</returns>
        string contentType { get; }
    }

    ///<summary>A helper object for form sections containing generic, non-file data.</summary>
    ///<remarks>This helper object is used similarly to the <see cref="WWWForm" /> method <see cref="WWWForm.AddBinaryData" />. It is used to define non-file form sections.</remarks>
    public class MultipartFormDataSection : IMultipartFormSection
    {
        private string name;
        private byte[] data;
        private string content;

        ///<summary>A raw data section with a section name and a <c>Content-Type</c> header.</summary>
        ///<param name="name">Section name.</param>
        ///<param name="data">Data payload of this section.</param>
        ///<param name="contentType">The value for this section's <c>Content-Type</c> header.</param>
        public MultipartFormDataSection(string name, byte[] data, string contentType)
        {
            if (data == null || data.Length < 1)
            {
                throw new ArgumentException("Cannot create a multipart form data section without body data");
            }

            this.name = name;
            this.data = data;
            this.content = contentType;
        }

        ///<summary>Raw data section with a section name, no <c>Content-Type</c> header.</summary>
        ///<remarks>Identical to the prior constructor, but with a section name included.</remarks>
        ///<param name="name">Section name.</param>
        ///<param name="data">Data payload of this section.</param>
        public MultipartFormDataSection(string name, byte[] data) : this(name, data, null)
        {}

        ///<summary>Raw data section, unnamed and no <c>Content-Type</c> header.</summary>
        ///<remarks>Will not include either a filename or a <c>Content-Type</c> section header.</remarks>
        ///<param name="data">Data payload of this section.</param>
        public MultipartFormDataSection(byte[] data) : this(null, data)
        {}

        ///<summary>A named raw data section whose payload is derived from a string, with a <c>Content-Type</c> header.</summary>
        ///<remarks>data will be encoded into raw bytes using <c>encoding</c>.</remarks>
        ///<param name="name">Section name.</param>
        ///<param name="data">String data payload for this section.</param>
        ///<param name="contentType">The value for this section's <c>Content-Type</c> header.</param>
        ///<param name="encoding">An encoding to marshal <c>data</c> to or from raw bytes.</param>
        public MultipartFormDataSection(string name, string data, System.Text.Encoding encoding, string contentType)
        {
            if (string.IsNullOrEmpty(data))
            {
                throw new ArgumentException("Cannot create a multipart form data section without body data");
            }

            byte[] dataBytes = encoding.GetBytes(data);

            this.name = name;
            this.data = dataBytes;

            if (contentType != null && !contentType.Contains("encoding="))
            {
                contentType = contentType.Trim() + "; encoding=" + encoding.WebName;
            }

            this.content = contentType;
        }

        ///<summary>A named raw data section whose payload is derived from a UTF8 string, with a <c>Content-Type</c> header.</summary>
        ///<remarks>For UTF-8 strings with custom <c>Content-Type</c> headers, use this constructor. The data is returned in UTF-8 encoding and converted to raw bytes appropriately.</remarks>
        ///<param name="name">Section name.</param>
        ///<param name="data">String data payload for this section.</param>
        ///<param name="contentType">The value for this section's <c>Content-Type</c> header.</param>
        public MultipartFormDataSection(string name, string data, string contentType) : this(name, data, System.Text.Encoding.UTF8, contentType)
        {}

        ///<summary>A names raw data section whose payload is derived from a UTF8 string, with a default <c>Content-Type</c>.</summary>
        ///<remarks>For UTF8 strings, use this constructor. <c>data</c> will be assumed to be in UTF8 encoding and converted appropriately. The section will be assigned a <c>Content-Type</c> of <c>text/plain; encoding=utf8</c>.</remarks>
        ///<param name="name">Section name.</param>
        ///<param name="data">String data payload for this section.</param>
        public MultipartFormDataSection(string name, string data) : this(name, data, "text/plain")
        {}

        ///<summary>An anonymous raw data section whose payload is derived from a UTF8 string, with a default <c>Content-Type</c>.</summary>
        ///<remarks>Identical to the above, but without a section name.</remarks>
        ///<param name="data">String data payload for this section.</param>
        public MultipartFormDataSection(string data) : this(null, data)
        {}

        ///<summary>Returns the name of this section, if any.</summary>
        ///<returns>The section's name, or <c>null</c>.</returns>
        ///<seealso cref="IMultipartFormSection.sectionName" />
        public string sectionName { get { return this.name; } }
        ///<summary>Returns the raw binary data contained in this section. Will not return null or a zero-length array.</summary>
        ///<returns>The raw binary data contained in this section. Will not be null or empty.</returns>
        ///<seealso cref="IMultipartFormSection.sectionData" />
        public byte[] sectionData { get { return this.data; } }
        ///<summary>Returns a string denoting the desired filename of this section on the destination server.</summary>
        ///<returns>The desired file name of this section, or <c>null</c> if this is not a file section.</returns>
        ///<seealso cref="IMultipartFormSection.fileName" />
        public string fileName { get { return null; } }
        ///<summary>Returns the value to use in this section's <c>Content-Type</c> header.</summary>
        ///<returns>The <c>Content-Type</c> header for this section, or <c>null</c>.</returns>
        ///<seealso cref="IMultipartFormSection.contentType" />
        public string contentType { get { return this.content; } }
    }

    ///<summary>A helper object for adding file uploads to multipart forms via the [IMultipartFormSection] API.</summary>
    ///<remarks>This object is similar to the <see cref="MultipartFormDataSection" /> object, but all constructors additionally accept (and require) a <c>fileName</c> parameter. If you omit the <c>fileName</c> parameter, this object provides a default filename.</remarks>
    public class MultipartFormFileSection : IMultipartFormSection
    {
        private string name;
        private byte[] data;
        private string file;
        private string content;

        private void Init(string name, byte[] data, string fileName, string contentType)
        {
            this.name = name;
            this.data = data;
            this.file = fileName;
            this.content = contentType;
        }

        ///<summary>Contains a named file section based on the raw bytes from <c>data</c>, with a custom <c>Content-Type</c> and file name.</summary>
        ///<remarks>The full-control option. Manually specify a section name, raw data, file name and <c>Content-Type</c>. If <c>fileName</c> is null or empty, it defaults to <c>file.dat</c>. If contentType is null or empty, it defaults to <c>application/octet-stream</c>.</remarks>
        ///<param name="name">Name of this form section.</param>
        ///<param name="data">Raw contents of the file to upload.</param>
        ///<param name="fileName">Name of the file uploaded by this form section.</param>
        ///<param name="contentType">The value for this section's <c>Content-Type</c> header.</param>
        public MultipartFormFileSection(string name, byte[] data, string fileName, string contentType)
        {
            if (data == null || data.Length < 1)
            {
                throw new ArgumentException("Cannot create a multipart form file section without body data");
            }

            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "file.dat";
            }

            if (string.IsNullOrEmpty(contentType))
            {
                contentType = "application/octet-stream";
            }

            Init(name, data, fileName, contentType);
        }

        ///<summary>Contains an anonymous file section based on the raw bytes from <c>data</c>, assigns a default <c>Content-Type</c> and file name.</summary>
        ///<remarks>Creates a file section based on the raw bytes from the <c>data</c> argument. Assigns a content-type of application/octet-stream and a file name of file.dat.</remarks>
        ///<param name="data">Raw contents of the file to upload.</param>
        public MultipartFormFileSection(byte[] data) : this(null, data, null, null)
        {}

        ///<summary>Contains an anonymous file section based on the raw bytes from <c>data</c> with a specific file name. Assigns a default <c>Content-Type</c>.</summary>
        ///<remarks>Assigns a <c>Content-Type</c> of <c>application/octet-stream</c>.</remarks>
        ///<param name="data">Raw contents of the file to upload.</param>
        ///<param name="fileName">Name of the file uploaded by this form section.</param>
        public MultipartFormFileSection(string fileName, byte[] data) : this(null, data, fileName, null)
        {}

        // String upload functions, for convenience
        ///<summary>Contains a named file section with data drawn from <c>data</c>, as marshaled by <c>dataEncoding</c>. Assigns a specific file name from <c>fileName</c> and a default <c>Content-Type</c>.</summary>
        ///<remarks>
        ///  <c>Content-Type</c> is assumed to be <c>text/plain</c>, with an <c>encoding</c> drawn from <c>dataEncoding</c>. If <c>dataEncoding</c> is null, defaults to UTF8.</remarks>
        ///<param name="name">Name of this form section.</param>
        ///<param name="data">Contents of the file to upload.</param>
        ///<param name="dataEncoding">A string encoding.</param>
        ///<param name="fileName">Name of the file uploaded by this form section.</param>
        public MultipartFormFileSection(string name, string data, System.Text.Encoding dataEncoding, string fileName)
        {
            if (string.IsNullOrEmpty(data))
            {
                throw new ArgumentException("Cannot create a multipart form file section without body data");
            }

            if (dataEncoding == null)
            {
                dataEncoding = System.Text.Encoding.UTF8;
            }

            byte[] dataBytes = dataEncoding.GetBytes(data);

            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "file.txt";
            }

            if (string.IsNullOrEmpty(this.content))
            {
                this.content = "text/plain; charset=" + dataEncoding.WebName;
            }

            Init(name, dataBytes, fileName, this.content);
        }

        ///<summary>An anonymous file section with data drawn from <c>data</c>, as marshaled by <c>dataEncoding</c>. Assigns a specific file name from <c>fileName</c> and a default <c>Content-Type</c>.</summary>
        ///<remarks>As above, but unnamed.</remarks>
        ///<param name="data">Contents of the file to upload.</param>
        ///<param name="dataEncoding">A string encoding.</param>
        ///<param name="fileName">Name of the file uploaded by this form section.</param>
        public MultipartFormFileSection(string data, System.Text.Encoding dataEncoding, string fileName) : this(null, data, dataEncoding, fileName)
        {}

        ///<summary>An anonymous file section with data drawn from the UTF8 string <c>data</c>. Assigns a specific file name from <c>fileName</c> and a default <c>Content-Type</c>.</summary>
        ///<remarks>Convenience method. Specify file contents via the <c>data</c> string and assign a file name via <c>fileName</c>. Assumes the string is encoded in UTF8. Assigns a <c>Content-Type</c> of <c>text/plain; encoding=utf8</c>. If <c>fileName</c> is null or empty, assigns a file name of <c>file.txt</c>.</remarks>
        ///<param name="data">Contents of the file to upload.</param>
        ///<param name="fileName">Name of the file uploaded by this form section.</param>
        public MultipartFormFileSection(string data, string fileName) : this(data, null, fileName)
        {}

        ///<summary>Returns the name of this section, if any.</summary>
        ///<returns>The section's name, or <c>null</c>.</returns>
        public string sectionName { get { return this.name; } }
        ///<summary>Returns the raw binary data contained in this section. Will not return null or a zero-length array.</summary>
        ///<returns>The raw binary data contained in this section. Will not be null or empty.</returns>
        public byte[] sectionData { get { return this.data; } }
        ///<summary>Returns a string denoting the desired filename of this section on the destination server.</summary>
        ///<returns>The desired file name of this section, or <c>null</c> if this is not a file section.</returns>
        public string fileName { get { return this.file; } }
        ///<summary>Returns the value of the section's <c>Content-Type</c> header.</summary>
        ///<returns>The <c>Content-Type</c> header for this section, or <c>null</c>.</returns>
        public string contentType { get { return this.content; } }
    }
}
