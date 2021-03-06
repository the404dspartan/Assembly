﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Blamite.Util;

namespace Blamite.Flexibility
{
    public static class VertexLayoutLoader
    {
        /// <summary>
        /// Loads all of the vertex layouts defined in an XML document.
        /// </summary>
        /// <param name="layoutDocument">The XML document to load vertex layouts from.</param>
        /// <returns>The layouts that were loaded.</returns>
        public static VertexLayoutCollection LoadLayouts(XDocument layoutDocument)
        {
            XContainer vertexTypesContainer = layoutDocument.Element("vertexTypes");
            if (layoutDocument == null)
                throw new ArgumentException("Invalid vertex layout document");

            VertexLayoutCollection result = new VertexLayoutCollection();
            foreach (XElement vertex in vertexTypesContainer.Elements("vertex"))
            {
                var layout = LoadLayout(vertex);
                result.AddLayout(layout);
            }
            return result;
        }

        /// <summary>
        /// Loads all of the vertex layouts defined in an XML document.
        /// </summary>
        /// <param name="documentPath">The path to the XML document to load.</param>
        /// <returns>The layouts that were loaded.</returns>
        public static VertexLayoutCollection LoadLayouts(string documentPath)
        {
            return LoadLayouts(XDocument.Load(documentPath));
        }

        /// <summary>
        /// Loads a VertexLayout based off of information in a vertex XML element.
        /// </summary>
        /// <param name="vertexElement">The vertex element to create the layout from.</param>
        /// <returns>The VertexLayout that was created.</returns>
        private static VertexLayout LoadLayout(XElement vertexElement)
        {
            // Vertex tags have the format:
            // <vertex type="(type index)" name="(vertex name)">(elements)</vertex>
            int type = XMLUtil.GetNumericAttribute(vertexElement, "type");
            string name = XMLUtil.GetStringAttribute(vertexElement, "name");
            VertexLayout result = new VertexLayout(type, name);

            result.AddElements(LoadLayoutElements(vertexElement));
            return result;
        }

        private static IEnumerable<VertexElementLayout> LoadLayoutElements(XContainer container)
        {
            // Element tags have the format:
            // <value stream="(stream)" offset="(offset)" type="(type)" usage="(usage)" usageIndex="(usage index)" />
            return (from element in container.Elements("value")
                    select new VertexElementLayout(
                        XMLUtil.GetNumericAttribute(element, "stream"),
                        XMLUtil.GetNumericAttribute(element, "offset"),
                        XMLUtil.GetEnumAttribute<VertexElementType>(element, "type"),
                        XMLUtil.GetEnumAttribute<VertexElementUsage>(element, "usage"),
                        XMLUtil.GetNumericAttribute(element, "usageIndex")));
        }
    }
}
