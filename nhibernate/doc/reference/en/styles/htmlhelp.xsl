<?xml version="1.0"?>

<!--
	This is a modification of Hibernate's html.xsl file to generate
	a MS CHM file.

	This is the XSL HTMLHelp configuration file for the NHibernate
	Reference Documentation.

	It took me days to figure out this stuff and fix most of
	the obvious bugs in the DocBook XSL distribution. Some of
	the workarounds might not be appropriate with a newer version
	of DocBook XSL. This file is released as part of Hibernate,
	hence LGPL licensed.

	christian@hibernate.org
-->

<!DOCTYPE xsl:stylesheet [
	<!ENTITY db_xsl_path        "../../support/docbook-xsl/">
]>

<xsl:stylesheet xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
				version="1.0"
				xmlns="http://www.w3.org/TR/xhtml1/transitional"
				exclude-result-prefixes="#default">
	
<xsl:import href="&db_xsl_path;/htmlhelp/htmlhelp.xsl"/>

<!-- HTML Settings -->   

	<!--
		modified this so the stylesheet would be lang specific since it is stored
		under the lang subdirectory anyway
	-->
	<xsl:param name="html.stylesheet" select="'html.css'" />
	<xsl:param name="htmlhelp.chm" select="'reference.chm'"></xsl:param>


	<xsl:param name="suppress.navigation" select="0"/>
	<xsl:param name="htmlhelp.hhc.binary" select="0"/>
	<xsl:param name="htmlhelp.hhc.folders.instead.books" select="0"/>
	<xsl:param name="img.src.path"></xsl:param>
	<xsl:param name="generate.index" select="1" />
	<!-- These extensions are required for table printing and other stuff -->

	<!-- Generate the TOCs for named components only -->
	<xsl:param name="generate.toc">
		book   toc
	</xsl:param>
		
	<!-- Show only Sections up to level 3 in the TOCs -->
	<xsl:param name="toc.section.depth">3</xsl:param>
	
<!-- Labels -->   

	<!-- Label Chapters and Sections (numbering) -->
	<xsl:param name="chapter.autolabel">1</xsl:param>
	<xsl:param name="section.autolabel" select="1"/>
	<xsl:param name="section.label.includes.component.label" select="1"/>

<!-- Callouts -->

	<!-- Don't use graphics, use a simple number style -->
	<xsl:param name="callout.graphics">0</xsl:param>

	<!-- Place callout marks at this column in annotated areas -->
	<xsl:param name="callout.defaultcolumn">90</xsl:param>

<!-- Misc -->   

	<!-- Placement of titles -->
	<xsl:param name="formal.title.placement">
		figure after
		example before
		equation before
		table before
		procedure before
	</xsl:param>    

</xsl:stylesheet>
