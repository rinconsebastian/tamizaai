/** A configuration section of the project, listed under Settings. */
export interface SectionLink {
  /** Translation key of the label. */
  labelKey: string;
  link: string;
}

/**
 * The configuration sections grouped under Settings, in the order members see them. Webhook is listed for admins only; the
 * other sections are readable by every member, as the capability specs require. Both the project page and the settings layout
 * build their lists from here so they never disagree.
 */
export function projectSections(id: string, isAdmin: boolean): SectionLink[] {
  const base = `/projects/${id}`;
  const sections: SectionLink[] = [
    { labelKey: 'project.tabs.overview', link: `${base}/overview` },
    { labelKey: 'project.tabs.data', link: `${base}/data` },
    { labelKey: 'project.tabs.scripts', link: `${base}/scripts` },
    { labelKey: 'project.tabs.reference', link: `${base}/reference` },
    { labelKey: 'project.tabs.samplingFrame', link: `${base}/sampling-frame` },
    { labelKey: 'project.tabs.members', link: `${base}/members` },
  ];
  if (isAdmin) {
    sections.push({ labelKey: 'project.tabs.webhook', link: `${base}/webhook` });
  }
  return sections;
}
